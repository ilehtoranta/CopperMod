using CopperMod.Abstractions;
using NAudio.Wave;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using CopperMod.Rendering;

namespace CopperMod;

internal sealed class PlayerWindow : Window, IDisposable
{
	private static readonly bool WaveformUiEnabled = true;
	private static readonly TimeSpan ViewRefreshInterval = TimeSpan.FromMilliseconds(200);
	private static readonly TimeSpan WaveformRefreshInterval = TimeSpan.FromMilliseconds(16);
	private const int MaximumPendingWaveforms = 12;
	private const float WaveformSmoothingAmount = 1.0f;
	private const int WaveformLabelRow = 13;
	private const int WaveformViewRow = 14;
	private static readonly WaveformSnapshot EmptyWaveform = new(
		Array.Empty<float>(),
		Array.Empty<float>(),
		sourceFrameCount: 0,
		sampleRate: ModuleAudioPlayer.SampleRate);
	private readonly ModuleAudioPlayer _player = new();
	private readonly IApplication _application;
	private readonly TextField _pathField;
	private readonly Label _titleLabel;
	private readonly Label _formatLabel;
	private readonly Label _subSongLabel;
	private readonly Label _stateLabel;
	private readonly Label _timeLabel;
	private readonly ProgressBar _progressBar;
	private readonly Button _playPauseButton;
	private readonly Button _outputProfileButton;
	private readonly Button _previousSubSongButton;
	private readonly Button _nextSubSongButton;
	private readonly Button _waveformModeButton;
	private readonly Button _infoButton;
	private readonly string? _initialPath;
	private readonly bool _autoPlay;
	private readonly object _waveformSync = new();
	private readonly Queue<WaveformSnapshot> _pendingWaveforms = new();
	private Label? _waveformLabel;
	private ImageView? _waveformView;
	private WaveformSnapshot? _targetWaveform;
	private WaveformSnapshot? _displayWaveform;
	private int _waveformImageWidth;
	private int _waveformImageHeight;
	private string? _lastErrorText;
	private volatile bool _waveformNeedsRender;

	public PlayerWindow(IApplication application, string? initialPath, bool autoPlay)
	{
		_application = application ?? throw new ArgumentNullException(nameof(application));
		_initialPath = initialPath;
		_autoPlay = autoPlay;

		Title = "CopperMod";
		Width = Dim.Fill();
		Height = Dim.Fill();

		_pathField = new TextField
		{
			X = 1,
			Y = 1,
			Width = Dim.Fill(14),
			Text = initialPath ?? string.Empty
		};

		var loadButton = new Button
		{
			X = Pos.AnchorEnd(12),
			Y = 1,
			Text = "Load"
		};
		loadButton.Accepted += (_, _) => LoadFromField(autoPlay: false);

		_playPauseButton = new Button
		{
			X = 1,
			Y = 3,
			Text = "Play"
		};
		_playPauseButton.Accepted += (_, _) => TogglePlayback();

		var stopButton = new Button
		{
			X = 11,
			Y = 3,
			Text = "Stop"
		};
		stopButton.Accepted += (_, _) => RunPlayerAction(_player.Stop);

		var rewindButton = new Button
		{
			X = 21,
			Y = 3,
			Text = "-10s"
		};
		rewindButton.Accepted += (_, _) => SeekRelative(TimeSpan.FromSeconds(-10));

		var forwardButton = new Button
		{
			X = 31,
			Y = 3,
			Text = "+10s"
		};
		forwardButton.Accepted += (_, _) => SeekRelative(TimeSpan.FromSeconds(10));

		_infoButton = new Button
		{
			X = Pos.AnchorEnd(22),
			Y = 3,
			Text = "Info"
		};
		_infoButton.Accepted += (_, _) => ShowInfoDialog();

		var quitButton = new Button
		{
			X = Pos.AnchorEnd(12),
			Y = 3,
			Text = "Quit"
		};
		quitButton.Accepted += (_, _) => _application.RequestStop(this);

		_outputProfileButton = new Button
		{
			X = 43,
			Y = 3,
			Text = "A500"
		};
		_outputProfileButton.Accepted += (_, _) => CycleOutputProfile();

		_waveformModeButton = new Button
		{
			X = 53,
			Y = 3,
			Text = "Mix"
		};
		_waveformModeButton.Accepted += (_, _) => CycleWaveformDisplayMode();

		_titleLabel = new Label
		{
			X = 1,
			Y = 5,
			Width = Dim.Fill(2),
			Text = "No module loaded"
		};

		_formatLabel = new Label
		{
			X = 1,
			Y = 6,
			Width = Dim.Fill(2),
			Text = string.Empty
		};

		_subSongLabel = new Label
		{
			X = 1,
			Y = 7,
			Width = Dim.Fill(2),
			Text = string.Empty
		};

		_previousSubSongButton = new Button
		{
			X = Pos.AnchorEnd(30),
			Y = 7,
			Text = "-Sub"
		};
		_previousSubSongButton.Accepted += (_, _) => SelectRelativeSubSong(-1);

		_nextSubSongButton = new Button
		{
			X = Pos.AnchorEnd(20),
			Y = 7,
			Text = "+Sub"
		};
		_nextSubSongButton.Accepted += (_, _) => SelectRelativeSubSong(1);

		_stateLabel = new Label
		{
			X = 1,
			Y = 8,
			Width = Dim.Fill(2),
			Text = "State: stopped"
		};

		_timeLabel = new Label
		{
			X = 1,
			Y = 9,
			Width = Dim.Fill(2),
			Text = "00:00 / --:--"
		};

		_progressBar = new ProgressBar
		{
			X = 1,
			Y = 11,
			Width = Dim.Fill(2)
		};

		Add(_pathField, loadButton, _playPauseButton, stopButton, rewindButton, forwardButton, _outputProfileButton, _infoButton, quitButton,
			_titleLabel, _formatLabel, _subSongLabel, _previousSubSongButton, _nextSubSongButton, _stateLabel, _timeLabel, _progressBar);

		if (WaveformUiEnabled)
		{
			_player.WaveformEnabled = true;
		}

		_player.StateChanged += (_, _) => _application.Invoke(RefreshView);
		if (WaveformUiEnabled)
		{
			TryEnableWaveformDisplay();
		}

		_application.AddTimeout(ViewRefreshInterval, () =>
		{
			RefreshView();
			return true;
		});

		if (WaveformUiEnabled)
		{
			_application.AddTimeout(WaveformRefreshInterval, () =>
			{
				RefreshWaveform();
				return true;
			});
		}

		if (!string.IsNullOrWhiteSpace(_initialPath) && File.Exists(_initialPath))
		{
			_application.AddTimeout(TimeSpan.FromMilliseconds(1), () =>
			{
				LoadPath(_initialPath, _autoPlay);
				return false;
			});
		}
	}

	private void TryEnableWaveformDisplay()
	{
		if (!WaveformUiEnabled || _waveformView != null || !SixelCapability.IsSupported(_application))
		{
			return;
		}

		_waveformLabel = new Label
		{
			X = 1,
			Y = WaveformLabelRow,
			Width = Dim.Fill(2),
			Text = "Waveform"
		};

		_waveformView = new ImageView
		{
			X = 1,
			Y = WaveformViewRow,
			Width = Dim.Fill(2),
			Height = Dim.Fill(1),
			UseSixel = true,
			SixelEncoder = WaveformSixelEncoder.Create()
		};
		_waveformView.Image = WaveformImageRenderer.Render(EmptyWaveform);

		Add(_waveformLabel, _waveformView);
		SetNeedsDraw();
	}

	public new void Dispose()
	{
		_player.Dispose();
		base.Dispose();
	}

	private void LoadFromField(bool autoPlay)
	{
		LoadPath(_pathField.Text?.ToString() ?? string.Empty, autoPlay);
	}

	private void LoadPath(string path, bool autoPlay)
	{
		RunPlayerAction(() =>
		{
			ClearWaveforms();
			_displayWaveform = null;
			if (_waveformView != null)
			{
				_waveformView.Image = WaveformImageRenderer.Render(EmptyWaveform);
			}

			_player.Load(path);
			if (autoPlay)
			{
				_player.Play();
			}
		});
	}

	private void TogglePlayback()
	{
		RunPlayerAction(() =>
		{
			if (!_player.IsLoaded)
			{
				LoadFromField(autoPlay: false);
			}

			if (_player.PlaybackState == PlaybackState.Playing)
			{
				_player.Pause();
			}
			else
			{
				_player.Play();
			}
		});
	}

	private void SeekRelative(TimeSpan delta)
	{
		RunPlayerAction(() => _player.Seek(_player.Position.Time + delta));
	}

	private void CycleOutputProfile()
	{
		RunPlayerAction(() =>
		{
			if (_player.OutputFamily == ModuleOutputFamily.Commodore64)
			{
				_player.C64OutputProfile = _player.C64OutputProfile == C64OutputProfile.C64
					? C64OutputProfile.Clean
					: C64OutputProfile.C64;
				return;
			}

			_player.OutputProfile = _player.OutputProfile switch
			{
				AmigaOutputProfile.A500 => AmigaOutputProfile.A500LedFilter,
				AmigaOutputProfile.A500LedFilter => AmigaOutputProfile.None,
				_ => AmigaOutputProfile.A500
			};
		});
	}

	private void SelectRelativeSubSong(int delta)
	{
		RunPlayerAction(() =>
		{
			var selector = _player.SubSongs;
			if (selector == null || selector.SubSongCount <= 1)
			{
				return;
			}

			var next = selector.CurrentSubSongIndex + delta;
			if (next < 0)
			{
				next = selector.SubSongCount - 1;
			}
			else if (next >= selector.SubSongCount)
			{
				next = 0;
			}

			_player.SelectSubSong(next);
		});
	}

	private void CycleWaveformDisplayMode()
	{
		RunPlayerAction(() =>
		{
			_player.WaveformDisplayMode = _player.WaveformDisplayMode switch
			{
				WaveformDisplayMode.MixedOutput => WaveformDisplayMode.TrackerChannels,
				_ => WaveformDisplayMode.MixedOutput
			};
			ClearWaveforms();
			_displayWaveform = null;
			if (_waveformView != null)
			{
				_waveformView.Image = WaveformImageRenderer.Render(EmptyWaveform);
			}
		});
	}

	private void RunPlayerAction(Action action)
	{
		try
		{
			action();
			RefreshView();
		}
		catch (Exception ex)
		{
			_lastErrorText = ex.ToString();
			_stateLabel.Text = "Error: " + ex.Message;
			MessageBox.ErrorQuery(_application, "Error", ex.Message, "OK");
		}
	}

	private void RefreshView()
	{
		if (WaveformUiEnabled)
		{
			TryEnableWaveformDisplay();
		}

		var metadata = _player.Metadata;
		_titleLabel.Text = metadata == null
			? "No module loaded"
			: $"{metadata.Title ?? Path.GetFileName(_player.FilePath) ?? "Untitled"}";
		_formatLabel.Text = metadata == null
			? string.Empty
			: $"{metadata.FormatVersion}  channels {metadata.ChannelCount}  instruments {metadata.InstrumentCount}";
		var subSongs = _player.SubSongs;
		var hasSubSongs = subSongs != null && subSongs.SubSongCount > 1;
		_subSongLabel.Visible = hasSubSongs;
		_previousSubSongButton.Visible = hasSubSongs;
		_nextSubSongButton.Visible = hasSubSongs;
		_subSongLabel.Text = hasSubSongs
			? $"Subtune {subSongs!.CurrentSubSongIndex + 1}/{subSongs.SubSongCount}"
			: string.Empty;

		_stateLabel.Text = "State: " + _player.PlaybackState.ToString().ToLowerInvariant();
		_playPauseButton.Text = _player.PlaybackState == PlaybackState.Playing ? "Pause" : "Play";
		_outputProfileButton.Text = _player.OutputFamily == ModuleOutputFamily.Commodore64
			? FormatC64OutputProfile(_player.C64OutputProfile)
			: FormatOutputProfile(_player.OutputProfile);
		if (WaveformUiEnabled)
		{
			_waveformModeButton.Text = FormatWaveformDisplayModeButton(_player.WaveformDisplayMode);
		}

		var position = _player.Position.Time;
		var duration = _player.Duration.Time;
		_timeLabel.Text = $"{FormatTime(position)} / {(duration.HasValue ? FormatTime(duration.Value) : "--:--")}";
		_progressBar.Fraction = duration.HasValue && duration.Value.TotalSeconds > 0
			? Math.Clamp((float)(position.TotalSeconds / duration.Value.TotalSeconds), 0.0f, 1.0f)
			: 0.0f;

		if (WaveformUiEnabled)
		{
			RefreshWaveform();
		}

		SetNeedsDraw();
	}

	private void RefreshWaveform()
	{
		if (_waveformView == null)
		{
			return;
		}

		var hasNewWaveform = false;
		if (_player.TryReadWaveformSnapshot(out var polledWaveform))
		{
			EnqueueWaveform(polledWaveform);
		}

		while (TryDequeueWaveform(out var nextWaveform))
		{
			_targetWaveform = nextWaveform;
			hasNewWaveform = true;
		}

		if (hasNewWaveform)
		{
			_waveformNeedsRender = true;
		}

		var (imageWidth, imageHeight) = ComputeWaveformImageSize();
		var imageSizeChanged = imageWidth != _waveformImageWidth || imageHeight != _waveformImageHeight;
		if (!_waveformNeedsRender && !imageSizeChanged && _displayWaveform != null)
		{
			return;
		}

		var target = _targetWaveform ?? EmptyWaveform;
		_displayWaveform = WaveformSmoother.MoveTowards(
			_displayWaveform,
			target,
			WaveformSmoothingAmount,
			out var settled);
		_waveformNeedsRender = !settled || HasPendingWaveforms();
		if (settled && !HasPendingWaveforms())
		{
			_displayWaveform = target;
			_waveformNeedsRender = false;
		}

		_waveformView.Image = WaveformImageRenderer.Render(_displayWaveform, imageWidth, imageHeight);
		_waveformImageWidth = imageWidth;
		_waveformImageHeight = imageHeight;
		_waveformView.SetNeedsDraw();
	}

	private (int Width, int Height) ComputeWaveformImageSize()
	{
		var viewColumns = _waveformView?.Frame.Width ?? 0;
		var viewRows = _waveformView?.Frame.Height ?? 0;
		if (viewColumns <= 0)
		{
			viewColumns = Math.Max(1, Frame.Width - 2);
		}

		if (viewRows <= 0)
		{
			viewRows = Math.Max(1, Frame.Height - WaveformViewRow - 1);
		}

		var resolution = _application.Driver?.SixelSupport?.Resolution;
		var cellWidth = resolution?.Width > 0 ? resolution.Value.Width : 10;
		var cellHeight = resolution?.Height > 0 ? resolution.Value.Height : 20;
		return WaveformImageSizer.Compute(viewColumns, viewRows, cellWidth, cellHeight);
	}

	private void EnqueueWaveform(WaveformSnapshot waveform)
	{
		lock (_waveformSync)
		{
			_pendingWaveforms.Enqueue(waveform);
			while (_pendingWaveforms.Count > MaximumPendingWaveforms)
			{
				_pendingWaveforms.Dequeue();
			}

			_waveformNeedsRender = true;
		}
	}

	private bool TryDequeueWaveform(out WaveformSnapshot waveform)
	{
		lock (_waveformSync)
		{
			if (_pendingWaveforms.Count == 0)
			{
				waveform = EmptyWaveform;
				return false;
			}

			waveform = _pendingWaveforms.Dequeue();
			return true;
		}
	}

	private bool HasPendingWaveforms()
	{
		lock (_waveformSync)
		{
			return _pendingWaveforms.Count > 0;
		}
	}

	private void ClearWaveforms()
	{
		lock (_waveformSync)
		{
			_pendingWaveforms.Clear();
			_targetWaveform = null;
			_waveformNeedsRender = false;
		}
	}

	private void ShowInfoDialog()
	{
		MessageBox.Query(_application, ComputeInfoDialogWidth(), ComputeInfoDialogHeight(), "Info", FormatInfo(), "OK");
	}

	private int ComputeInfoDialogWidth()
	{
		return Math.Clamp(Frame.Width - 8, 50, 100);
	}

	private int ComputeInfoDialogHeight()
	{
		return Math.Clamp(Frame.Height - 6, 12, 28);
	}

	private string FormatInfo()
	{
		var metadata = _player.Metadata;
		var lines = new List<string>
		{
			"File: " + (_player.FilePath ?? "none"),
			"Title: " + (metadata?.Title ?? "none"),
			"Format: " + (metadata == null ? "none" : metadata.FormatVersion ?? metadata.FormatName ?? "unknown"),
			"Channels: " + (metadata?.ChannelCount.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"),
			"Instruments: " + (metadata?.InstrumentCount.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"),
			"State: " + _player.PlaybackState.ToString().ToLowerInvariant(),
			"Position: " + FormatTime(_player.Position.Time) + " / " + (_player.Duration.Time.HasValue ? FormatTime(_player.Duration.Time.Value) : "--:--"),
			"Buffer: " + FormatBufferStatus(_player.BufferStatus),
			"Output: " + (_player.OutputFamily == ModuleOutputFamily.Commodore64
				? FormatC64OutputProfile(_player.C64OutputProfile)
				: FormatOutputProfile(_player.OutputProfile)),
			string.Empty,
			FormatDiagnostics(_player.Diagnostics)
		};

		if (_player.SubSongs is { SubSongCount: > 1 } subSongs)
		{
			lines.Insert(7, "Subtune: " + (subSongs.CurrentSubSongIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) +
				" / " + subSongs.SubSongCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}

		if (WaveformUiEnabled)
		{
			lines.Insert(lines.Count - 2, "Scope: " + FormatWaveformDisplayMode(_player.WaveformDisplayMode));
		}

		if (!string.IsNullOrWhiteSpace(_lastErrorText))
		{
			lines.Add(string.Empty);
			lines.Add("Last error:");
			lines.Add(_lastErrorText);
		}

		return string.Join(Environment.NewLine, lines);
	}

	private static string FormatDiagnostics(IReadOnlyList<ModuleDiagnostic> diagnostics)
	{
		if (diagnostics.Count == 0)
		{
			return "Diagnostics: none";
		}

		return "Diagnostics: " + string.Join(Environment.NewLine, diagnostics.Take(4).Select(d => d.ToString()));
	}

	private static string FormatTime(TimeSpan time)
	{
		return $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}";
	}

	private static string FormatBufferStatus(PlaybackBufferStatus status)
	{
		return status.QueuedDuration.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
			"s / " +
			status.TargetDuration.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
			"s, underruns " +
			status.UnderrunCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	private static string FormatOutputProfile(AmigaOutputProfile profile)
	{
		return profile switch
		{
			AmigaOutputProfile.A500 => "A500",
			AmigaOutputProfile.A500LedFilter => "LED",
			_ => "Clean"
		};
	}

	private static string FormatC64OutputProfile(C64OutputProfile profile)
	{
		return profile == C64OutputProfile.C64 ? "C64" : "Clean";
	}

	private static string FormatWaveformDisplayModeButton(WaveformDisplayMode mode)
	{
		return mode == WaveformDisplayMode.TrackerChannels ? "Ch" : "Mix";
	}

	private static string FormatWaveformDisplayMode(WaveformDisplayMode mode)
	{
		return mode == WaveformDisplayMode.TrackerChannels ? "channels" : "mixed output";
	}
}
