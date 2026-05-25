using AmigaTracker.Abstractions;
using NAudio.Wave;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace CopperMod;

internal sealed class PlayerWindow : Window, IDisposable
{
	private readonly ModuleAudioPlayer _player = new();
	private readonly IApplication _application;
	private readonly TextField _pathField;
	private readonly Label _titleLabel;
	private readonly Label _formatLabel;
	private readonly Label _stateLabel;
	private readonly Label _timeLabel;
	private readonly Label _diagnosticsLabel;
	private readonly ProgressBar _progressBar;
	private readonly Button _playPauseButton;
	private readonly Button _outputProfileButton;
	private readonly string? _initialPath;
	private readonly bool _autoPlay;

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

		var quitButton = new Button
		{
			X = 55,
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

		_diagnosticsLabel = new Label
		{
			X = 1,
			Y = 13,
			Width = Dim.Fill(2),
			Height = 5,
			Text = string.Empty
		};

		Add(_pathField, loadButton, _playPauseButton, stopButton, rewindButton, forwardButton, _outputProfileButton, quitButton,
			_titleLabel, _formatLabel, _stateLabel, _timeLabel, _progressBar, _diagnosticsLabel);

		_player.StateChanged += (_, _) => _application.Invoke(RefreshView);

		_application.AddTimeout(TimeSpan.FromMilliseconds(200), () =>
		{
			RefreshView();
			return true;
		});

		if (!string.IsNullOrWhiteSpace(_initialPath) && File.Exists(_initialPath))
		{
			_application.AddTimeout(TimeSpan.FromMilliseconds(1), () =>
			{
				LoadPath(_initialPath, _autoPlay);
				return false;
			});
		}
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
			_player.OutputProfile = _player.OutputProfile switch
			{
				AmigaOutputProfile.A500 => AmigaOutputProfile.A500LedFilter,
				AmigaOutputProfile.A500LedFilter => AmigaOutputProfile.None,
				_ => AmigaOutputProfile.A500
			};
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
			_stateLabel.Text = "Error: " + ex.Message;
			_diagnosticsLabel.Text = ex.ToString();
		}
	}

	private void RefreshView()
	{
		var metadata = _player.Metadata;
		_titleLabel.Text = metadata == null
			? "No module loaded"
			: $"{metadata.Title ?? Path.GetFileName(_player.FilePath) ?? "Untitled"}";
		_formatLabel.Text = metadata == null
			? string.Empty
			: $"{metadata.FormatVersion}  channels {metadata.ChannelCount}  instruments {metadata.InstrumentCount}";

		_stateLabel.Text = "State: " + _player.PlaybackState.ToString().ToLowerInvariant();
		_playPauseButton.Text = _player.PlaybackState == PlaybackState.Playing ? "Pause" : "Play";
		_outputProfileButton.Text = FormatOutputProfile(_player.OutputProfile);

		var position = _player.Position.Time;
		var duration = _player.Duration.Time;
		_timeLabel.Text = $"{FormatTime(position)} / {(duration.HasValue ? FormatTime(duration.Value) : "--:--")}";
		_progressBar.Fraction = duration.HasValue && duration.Value.TotalSeconds > 0
			? Math.Clamp((float)(position.TotalSeconds / duration.Value.TotalSeconds), 0.0f, 1.0f)
			: 0.0f;

		_diagnosticsLabel.Text = FormatDiagnostics(_player.Diagnostics);
		SetNeedsDraw();
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

	private static string FormatOutputProfile(AmigaOutputProfile profile)
	{
		return profile switch
		{
			AmigaOutputProfile.A500 => "A500",
			AmigaOutputProfile.A500LedFilter => "LED",
			_ => "Clean"
		};
	}
}
