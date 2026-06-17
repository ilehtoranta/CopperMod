using CopperMod.Abstractions;
using CopperMod.Sid;
using NAudio.Wave;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using CopperMod.Rendering;

namespace CopperMod;

internal sealed class PlayerWindow : Window, IDisposable
{
	private static readonly bool WaveformUiEnabled = true;
	private static readonly TimeSpan ViewRefreshInterval = TimeSpan.FromMilliseconds(200);
	private static readonly TimeSpan WaveformRefreshInterval = TimeSpan.FromMilliseconds(16);
	private static readonly TimeSpan WaveformDisplayDelay = TimeSpan.FromMilliseconds(250);
	private const int MaximumPendingWaveforms = 64;
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
	private readonly Button _c64FocusButton;
	private readonly Button _infoButton;
	private readonly string? _initialPath;
	private readonly bool _autoPlay;
	private readonly object _waveformSync = new();
	private readonly Queue<PendingWaveformSnapshot> _pendingWaveforms = new();
	private Label? _waveformLabel;
	private ImageView? _waveformView;
	private WaveformSnapshot? _targetWaveform;
	private WaveformSnapshot? _displayWaveform;
	private int _waveformImageWidth;
	private int _waveformImageHeight;
	private string? _lastErrorText;
	private volatile bool _waveformNeedsRender;
	private readonly PlayerStartupOptions _startupOptions;
	private readonly Dictionary<C64Key, DateTimeOffset> _syntheticKeyReleases = new();
	private PlayerDisplayMode _displayMode = PlayerDisplayMode.Auto;
	private long _lastVideoFrameNumber = -1;
	private bool _c64KeyboardFocusActive;
	private bool _disposed;

	public PlayerWindow(IApplication application, PlayerStartupOptions startupOptions, bool autoPlay)
	{
		_application = application ?? throw new ArgumentNullException(nameof(application));
		_startupOptions = startupOptions ?? throw new ArgumentNullException(nameof(startupOptions));
		_initialPath = startupOptions.InitialPath;
		_autoPlay = autoPlay;

		Title = "CopperMod";
		Width = Dim.Fill();
		Height = Dim.Fill();

		_pathField = new TextField
		{
			X = 1,
			Y = 1,
			Width = Dim.Fill(14),
			Text = _initialPath ?? string.Empty
		};

		var loadButton = new Button
		{
			X = Pos.AnchorEnd(12),
			Y = 1,
			Text = "Load"
		};
		loadButton.Accepted += (_, _) => LoadFromField(autoPlay: false);

		var previousFileButton = new Button
		{
			X = 1,
			Y = 2,
			Text = "Prev file"
		};
		previousFileButton.Accepted += (_, _) => LoadAdjacentFile(-1);

		var nextFileButton = new Button
		{
			X = 14,
			Y = 2,
			Text = "Next file"
		};
		nextFileButton.Accepted += (_, _) => LoadAdjacentFile(1);

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

		_c64FocusButton = new Button
		{
			X = 1,
			Y = 4,
			Text = "Focus C64",
			Visible = false
		};
		_c64FocusButton.Accepted += (_, _) => ToggleC64KeyboardFocus();

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

		Add(_pathField, loadButton, previousFileButton, nextFileButton, _playPauseButton, stopButton, rewindButton, forwardButton, _outputProfileButton, _waveformModeButton, _c64FocusButton, _infoButton, quitButton,
			_titleLabel, _formatLabel, _subSongLabel, _previousSubSongButton, _nextSubSongButton, _stateLabel, _timeLabel, _progressBar);

		if (WaveformUiEnabled)
		{
			_player.WaveformEnabled = true;
		}

		_player.StateChanged += (_, _) => _application.Invoke(RefreshView);
		_application.Keyboard.KeyDown += OnApplicationKeyDown;
		_application.Keyboard.KeyUp += OnApplicationKeyUp;
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
				ReleaseExpiredSyntheticC64Keys();
				RefreshDisplayImage();
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
			Text = "Display"
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
		_waveformView.Image = C64VideoImageRenderer.Render(null);

		Add(_waveformLabel, _waveformView);
		SetNeedsDraw();
	}

	public new void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_application.Keyboard.KeyDown -= OnApplicationKeyDown;
		_application.Keyboard.KeyUp -= OnApplicationKeyUp;
		_player.ReleaseAllC64Keys();
		_player.Dispose();
		base.Dispose();
	}

	protected override bool OnKeyDown(Key key)
	{
		if (TryHandleC64KeyboardFocusKey(key, pressed: true, synthesizeRelease: true))
		{
			return true;
		}

		return base.OnKeyDown(key);
	}

	protected override bool OnKeyUp(Key key)
	{
		if (TryHandleC64KeyboardFocusKey(key, pressed: false, synthesizeRelease: false))
		{
			return true;
		}

		return base.OnKeyUp(key);
	}

	private void LoadFromField(bool autoPlay)
	{
		LoadPath(_pathField.Text?.ToString() ?? string.Empty, autoPlay);
	}

	private void LoadPath(string path, bool autoPlay)
	{
		RunPlayerAction(() => LoadPathCore(path, autoPlay));
	}

	private void LoadAdjacentFile(int delta)
	{
		RunPlayerAction(() =>
		{
			var currentPath = _player.FilePath;
			if (string.IsNullOrWhiteSpace(currentPath))
			{
				currentPath = _pathField.Text?.ToString();
			}

			var nextPath = delta > 0
				? ModuleFileNavigator.ResolveNextFilePath(currentPath)
				: ModuleFileNavigator.ResolvePreviousFilePath(currentPath);
			if (nextPath == null)
			{
				var direction = delta > 0 ? "next" : "previous";
				throw new InvalidOperationException("No " + direction + " file was found.");
			}

			var autoPlay = _player.PlaybackState == PlaybackState.Playing;
			LoadPathCore(nextPath, autoPlay);
		});
	}

	private void LoadPathCore(string path, bool autoPlay)
	{
		ClearWaveforms();
		_displayWaveform = null;
		if (_waveformView != null)
		{
			_waveformView.Image = WaveformImageRenderer.Render(EmptyWaveform);
		}

		_player.Load(path, _startupOptions);
		SetC64KeyboardFocus(false, refresh: false);
		_displayMode = _player.HasC64Video ? PlayerDisplayMode.C64Video : PlayerDisplayMode.Waveform;
		_lastVideoFrameNumber = -1;
		_pathField.Text = _player.FilePath ?? Path.GetFullPath(path);
		if (autoPlay)
		{
			_player.Play();
		}
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
			if (_player.HasC64Video)
			{
				_displayMode = ResolveDisplayMode() == PlayerDisplayMode.C64Video
					? PlayerDisplayMode.Waveform
					: PlayerDisplayMode.C64Video;
				_lastVideoFrameNumber = -1;
			}
			else
			{
				_player.WaveformDisplayMode = _player.WaveformDisplayMode switch
				{
					WaveformDisplayMode.MixedOutput => WaveformDisplayMode.TrackerChannels,
					_ => WaveformDisplayMode.MixedOutput
				};
			}

			_waveformImageWidth = 0;
			_waveformImageHeight = 0;
			ClearWaveforms();
			_displayWaveform = null;
			if (_waveformView != null)
			{
				_waveformView.Image = ResolveDisplayMode() == PlayerDisplayMode.C64Video
					? C64VideoImageRenderer.Render(null)
					: WaveformImageRenderer.Render(EmptyWaveform);
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

	private void OnApplicationKeyDown(object? sender, Key key)
	{
		if (TryHandleC64KeyboardFocusKey(key, pressed: true, synthesizeRelease: true))
		{
			key.Handled = true;
		}
	}

	private void OnApplicationKeyUp(object? sender, Key key)
	{
		if (TryHandleC64KeyboardFocusKey(key, pressed: false, synthesizeRelease: false))
		{
			key.Handled = true;
		}
	}

	private void ToggleC64KeyboardFocus()
	{
		SetC64KeyboardFocus(!_c64KeyboardFocusActive);
	}

	private void SetC64KeyboardFocus(bool active, bool refresh = true)
	{
		active = active && _player.HasC64Video;
		if (_c64KeyboardFocusActive == active)
		{
			return;
		}

		_c64KeyboardFocusActive = active;
		if (active)
		{
			_displayMode = PlayerDisplayMode.C64Video;
			_lastVideoFrameNumber = -1;
		}
		else
		{
			_syntheticKeyReleases.Clear();
			_player.ReleaseAllC64Keys();
		}

		if (refresh)
		{
			RefreshView();
		}
	}

	private bool TryHandleC64KeyboardFocusKey(Key hostKey, bool pressed, bool synthesizeRelease)
	{
		if (!_c64KeyboardFocusActive)
		{
			return false;
		}

		if (!_player.HasC64Video)
		{
			SetC64KeyboardFocus(false);
			return false;
		}

		if (hostKey == Key.Esc)
		{
			if (pressed)
			{
				SetC64KeyboardFocus(false);
			}

			return true;
		}

		TryHandleC64Key(hostKey, pressed, synthesizeRelease);
		return true;
	}

	private bool TryHandleC64Key(Key hostKey, bool pressed, bool synthesizeRelease)
	{
		if (!_c64KeyboardFocusActive || !_player.HasC64Video || !TryMapC64Key(hostKey, out var c64Key, out var shifted))
		{
			return false;
		}

		if (shifted.HasValue)
		{
			SetC64KeyState(shifted.Value, pressed, synthesizeRelease);
		}

		SetC64KeyState(c64Key, pressed, synthesizeRelease);
		return true;
	}

	private void SetC64KeyState(C64Key key, bool pressed, bool synthesizeRelease)
	{
		_player.SetC64KeyPressed(key, pressed);
		if (!pressed)
		{
			_syntheticKeyReleases.Remove(key);
			return;
		}

		if (synthesizeRelease)
		{
			_syntheticKeyReleases[key] = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(120);
		}
	}

	private void ReleaseExpiredSyntheticC64Keys()
	{
		if (_syntheticKeyReleases.Count == 0)
		{
			return;
		}

		var now = DateTimeOffset.UtcNow;
		foreach (var key in _syntheticKeyReleases.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
		{
			_syntheticKeyReleases.Remove(key);
			_player.SetC64KeyPressed(key, pressed: false);
		}
	}

	private static bool TryMapC64Key(Key hostKey, out C64Key c64Key, out C64Key? shifted)
	{
		shifted = null;
		if (hostKey == Key.F1)
		{
			c64Key = C64Key.F1;
			return true;
		}

		if (hostKey == Key.F2)
		{
			c64Key = C64Key.F1;
			shifted = C64Key.LeftShift;
			return true;
		}

		if (hostKey == Key.F3)
		{
			c64Key = C64Key.F3;
			return true;
		}

		if (hostKey == Key.F4)
		{
			c64Key = C64Key.F3;
			shifted = C64Key.LeftShift;
			return true;
		}

		if (hostKey == Key.F5)
		{
			c64Key = C64Key.F5;
			return true;
		}

		if (hostKey == Key.F6)
		{
			c64Key = C64Key.F5;
			shifted = C64Key.LeftShift;
			return true;
		}

		if (hostKey == Key.F7)
		{
			c64Key = C64Key.F7;
			return true;
		}

		if (hostKey == Key.F8)
		{
			c64Key = C64Key.F7;
			shifted = C64Key.LeftShift;
			return true;
		}

		if (hostKey == Key.CursorRight)
		{
			c64Key = C64Key.CursorRight;
			return true;
		}

		if (hostKey == Key.CursorLeft)
		{
			c64Key = C64Key.CursorRight;
			shifted = C64Key.LeftShift;
			return true;
		}

		if (hostKey == Key.CursorDown)
		{
			c64Key = C64Key.CursorDown;
			return true;
		}

		if (hostKey == Key.CursorUp)
		{
			c64Key = C64Key.CursorDown;
			shifted = C64Key.LeftShift;
			return true;
		}

		if (hostKey == Key.Enter)
		{
			c64Key = C64Key.Return;
			return true;
		}

		if (hostKey == Key.Delete || hostKey == Key.DeleteChar || hostKey == Key.Backspace)
		{
			c64Key = C64Key.Delete;
			return true;
		}

		if (hostKey == Key.Home)
		{
			c64Key = C64Key.Home;
			return true;
		}

		if (hostKey == Key.Esc)
		{
			c64Key = C64Key.RunStop;
			return true;
		}

		var grapheme = hostKey.AsGrapheme;
		if (!string.IsNullOrEmpty(grapheme))
		{
			return TryMapC64Character(grapheme[0], out c64Key, out shifted);
		}

		c64Key = default;
		return false;
	}

	private static bool TryMapC64Character(char ch, out C64Key c64Key, out C64Key? shifted)
	{
		shifted = null;
		if (ch >= 'a' && ch <= 'z')
		{
			c64Key = (C64Key)Enum.Parse(typeof(C64Key), char.ToUpperInvariant(ch).ToString());
			return true;
		}

		if (ch >= 'A' && ch <= 'Z')
		{
			c64Key = (C64Key)Enum.Parse(typeof(C64Key), ch.ToString());
			shifted = C64Key.LeftShift;
			return true;
		}

		c64Key = ch switch
		{
			'0' => C64Key.Zero,
			'1' => C64Key.One,
			'2' => C64Key.Two,
			'3' => C64Key.Three,
			'4' => C64Key.Four,
			'5' => C64Key.Five,
			'6' => C64Key.Six,
			'7' => C64Key.Seven,
			'8' => C64Key.Eight,
			'9' => C64Key.Nine,
			' ' => C64Key.Space,
			'\r' or '\n' => C64Key.Return,
			'+' => C64Key.Plus,
			'-' => C64Key.Minus,
			'.' => C64Key.Period,
			':' => C64Key.Colon,
			'@' => C64Key.At,
			',' => C64Key.Comma,
			'*' => C64Key.Asterisk,
			';' => C64Key.Semicolon,
			'=' => C64Key.Equals,
			'/' => C64Key.Slash,
			_ => default
		};

		if (!EqualityComparer<C64Key>.Default.Equals(c64Key, default) || ch == '0')
		{
			return true;
		}

		return TryMapShiftedC64Character(ch, out c64Key, out shifted);
	}

	private static bool TryMapShiftedC64Character(char ch, out C64Key c64Key, out C64Key? shifted)
	{
		shifted = C64Key.LeftShift;
		c64Key = ch switch
		{
			'!' => C64Key.One,
			'"' => C64Key.Two,
			'#' => C64Key.Three,
			'$' => C64Key.Four,
			'%' => C64Key.Five,
			'&' => C64Key.Six,
			'\'' => C64Key.Seven,
			'(' => C64Key.Eight,
			')' => C64Key.Nine,
			'<' => C64Key.Comma,
			'>' => C64Key.Period,
			'?' => C64Key.Slash,
			_ => default
		};

		if (!EqualityComparer<C64Key>.Default.Equals(c64Key, default))
		{
			return true;
		}

		shifted = null;
		return false;
	}

	private void RefreshView()
	{
		if (!_player.HasC64Video && _c64KeyboardFocusActive)
		{
			SetC64KeyboardFocus(false, refresh: false);
		}

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
			_waveformModeButton.Text = FormatDisplayModeButton(ResolveDisplayMode(), _player.WaveformDisplayMode);
			_c64FocusButton.Visible = _player.HasC64Video;
			_c64FocusButton.Text = _c64KeyboardFocusActive ? "Release C64" : "Focus C64";
			if (_waveformLabel != null)
			{
				_waveformLabel.Text = ResolveDisplayMode() == PlayerDisplayMode.C64Video
					? (_c64KeyboardFocusActive ? "C64 video input" : "C64 video")
					: "Waveform";
			}
		}

		var position = _player.Position.Time;
		var duration = _player.Duration.Time;
		_timeLabel.Text = $"{FormatTime(position)} / {(duration.HasValue ? FormatTime(duration.Value) : "--:--")}";
		_progressBar.Fraction = duration.HasValue && duration.Value.TotalSeconds > 0
			? Math.Clamp((float)(position.TotalSeconds / duration.Value.TotalSeconds), 0.0f, 1.0f)
			: 0.0f;

		if (WaveformUiEnabled)
		{
			RefreshDisplayImage();
		}

		SetNeedsDraw();
	}

	private PlayerDisplayMode ResolveDisplayMode()
	{
		if (_displayMode == PlayerDisplayMode.Auto)
		{
			return _player.HasC64Video ? PlayerDisplayMode.C64Video : PlayerDisplayMode.Waveform;
		}

		if (_displayMode == PlayerDisplayMode.C64Video && !_player.HasC64Video)
		{
			return PlayerDisplayMode.Waveform;
		}

		return _displayMode;
	}

	private void RefreshDisplayImage()
	{
		if (ResolveDisplayMode() == PlayerDisplayMode.C64Video)
		{
			RefreshC64Video();
			return;
		}

		RefreshWaveform();
	}

	private void RefreshC64Video()
	{
		if (_waveformView == null)
		{
			return;
		}

		if (!_player.TryReadC64VideoFrame(out var frame))
		{
			if (_lastVideoFrameNumber >= 0)
			{
				_lastVideoFrameNumber = -1;
				_waveformView.Image = C64VideoImageRenderer.Render(null);
				_waveformView.SetNeedsDraw();
			}

			return;
		}

		if (frame.FrameNumber == _lastVideoFrameNumber)
		{
			return;
		}

		_lastVideoFrameNumber = frame.FrameNumber;
		_waveformView.Image = C64VideoImageRenderer.Render(frame);
		_waveformImageWidth = frame.Width;
		_waveformImageHeight = frame.Height;
		_waveformView.SetNeedsDraw();
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
			_pendingWaveforms.Enqueue(new PendingWaveformSnapshot(waveform, DateTimeOffset.UtcNow + WaveformDisplayDelay));
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

			var pending = _pendingWaveforms.Peek();
			if (pending.VisibleAt > DateTimeOffset.UtcNow)
			{
				waveform = EmptyWaveform;
				return false;
			}

			waveform = _pendingWaveforms.Dequeue().Waveform;
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
			lines.Insert(lines.Count - 2, "Display: " + FormatDisplayMode(ResolveDisplayMode(), _player.WaveformDisplayMode));
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

	private static string FormatDisplayModeButton(PlayerDisplayMode displayMode, WaveformDisplayMode waveformMode)
	{
		return displayMode == PlayerDisplayMode.C64Video
			? "C64"
			: FormatWaveformDisplayModeButton(waveformMode);
	}

	private static string FormatWaveformDisplayMode(WaveformDisplayMode mode)
	{
		return mode == WaveformDisplayMode.TrackerChannels ? "channels" : "mixed output";
	}

	private static string FormatDisplayMode(PlayerDisplayMode displayMode, WaveformDisplayMode waveformMode)
	{
		return displayMode == PlayerDisplayMode.C64Video
			? "C64 video"
			: FormatWaveformDisplayMode(waveformMode);
	}

	private readonly struct PendingWaveformSnapshot
	{
		public PendingWaveformSnapshot(WaveformSnapshot waveform, DateTimeOffset visibleAt)
		{
			Waveform = waveform;
			VisibleAt = visibleAt;
		}

		public WaveformSnapshot Waveform { get; }

		public DateTimeOffset VisibleAt { get; }
	}
}
