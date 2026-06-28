using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CopperPad;

namespace CopperPad.Gui;

internal sealed class MainWindow : Window, IDisposable
{
	private static readonly TimeSpan RawReportUiInterval = TimeSpan.FromMilliseconds(50);
	private static readonly TimeSpan ReportTextUpdateInterval = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan CaptureStatusUpdateInterval = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan ExplicitCaptureStatusHold = TimeSpan.FromSeconds(1.5);
	private static readonly TimeSpan GuidedMappingArmDelay = TimeSpan.FromMilliseconds(900);
	private static readonly TimeSpan GuidedMappingReadyDelay = TimeSpan.FromMilliseconds(900);
	private static readonly TimeSpan GuidedMappingLockDelay = TimeSpan.FromSeconds(1);
	private static readonly ControllerElement[] AxisTargets =
	[
		ControllerElement.LeftStickX,
		ControllerElement.LeftStickY,
		ControllerElement.RightStickX,
		ControllerElement.RightStickY,
		ControllerElement.LeftTrigger,
		ControllerElement.RightTrigger
	];

	private readonly FileControllerProfileStore _profileStore = new(CopperPadProfilePaths.GetDefaultProfilePath());
	private readonly ControllerDiagnosticsHost _host;
	private readonly ListBox _deviceList = new();
	private readonly TextBlock _statusText = TextBlock();
	private readonly TextBlock _deviceFilterText = TextBlock();
	private readonly CheckBox _showAllDevicesCheck = new() { Content = "Show all HID" };
	private readonly StackPanel _controllerSummary = new() { Spacing = 12, Margin = new Thickness(8) };
	private readonly TextBlock _controllerSummaryText = TextBlock();
	private readonly StackPanel _controllerActions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
	private readonly Button _testProfileButton = new() { Content = "View / Test Controls", IsEnabled = false };
	private readonly Button _createProfileButton = new() { Content = "Create Profile", IsEnabled = false };
	private readonly Button _editProfileButton = new() { Content = "Edit Profile", IsEnabled = false };
	private readonly TabControl _tabs = new();
	private readonly TextBlock _stateText = TextBlock();
	private readonly TextBlock _rawHexText = MonospaceTextBlock();
	private readonly TextBlock _changedBytesText = TextBlock();
	private readonly TextBlock _descriptorText = MonospaceTextBlock();
	private readonly TextBlock _reportRateText = TextBlock();
	private readonly TextBlock _captureStatusText = TextBlock();
	private readonly TextBlock _guidedPromptText = TextBlock();
	private readonly TextBlock _validationText = TextBlock();
	private readonly TextBlock _calibrationStatusText = TextBlock();
	private readonly TextBlock _calibrationPreviewText = TextBlock();
	private readonly StickView _leftStick = new();
	private readonly StickView _rightStick = new();
	private readonly ProgressBar _leftTrigger = new() { Minimum = 0, Maximum = 1, Height = 16 };
	private readonly ProgressBar _rightTrigger = new() { Minimum = 0, Maximum = 1, Height = 16 };
	private readonly Dictionary<ControllerElement, Border> _indicators = new();
	private readonly ComboBox _targetBox = new() { MinWidth = 220 };
	private readonly ComboBox _sourceKindBox = new() { MinWidth = 220 };
	private readonly NumericUpDown _offsetBox = NumberBox(0, 512);
	private readonly NumericUpDown _bitBox = NumberBox(0, 7);
	private readonly NumericUpDown _hatBox = NumberBox(0, 8);
	private readonly CheckBox _sourceInvertCheck = new() { Content = "Active low / inverted" };
	private readonly ListBox _bindingsList = new() { MinHeight = 140, MaxHeight = 260 };
	private readonly Button _startGuidedMappingButton = new() { Content = "Start Guided Mapping" };
	private readonly Button _skipGuidedMappingButton = new() { Content = "Skip", IsEnabled = false };
	private readonly Button _stopGuidedMappingButton = new() { Content = "Stop", IsEnabled = false };
	private readonly Button _useSuggestionButton = new() { Content = "Use Change", IsEnabled = false };
	private readonly Button _ignoreSuggestionButton = new() { Content = "Ignore Change", IsEnabled = false };
	private readonly Button _saveProfileButton = new() { Content = "Save Profile" };
	private readonly Button _newOverrideButton = new() { Content = "New Override", IsEnabled = false };
	private readonly ComboBox _calibrationTargetBox = new() { MinWidth = 170 };
	private readonly CheckBox _invertCheck = new() { Content = "Invert" };
	private readonly Slider _deadzoneSlider = new() { Minimum = 0, Maximum = 0.95, Value = 0.1, Width = 180 };
	private readonly Slider _saturationSlider = new() { Minimum = 0.1, Maximum = 1, Value = 1, Width = 180 };
	private readonly object _rawReportGate = new();
	private ControllerProfileSet _profiles = ControllerProfileSet.Empty;
	private IReadOnlyList<HidDeviceInfo> _allDevices = Array.Empty<HidDeviceInfo>();
	private ControllerProfile? _draftProfile;
	private HidDeviceInfo? _selectedDevice;
	private PendingRawReport? _pendingRawReport;
	private byte[]? _lastReport;
	private byte[]? _previousReport;
	private byte[]? _baselineReport;
	private byte[]? _guidedReleaseBaselineReport;
	private ControllerBindingSource? _suggestedSource;
	private ControllerBindingSource? _guidedReleaseSource;
	private readonly GuidedMappingCapture _guidedCapture = new(GuidedMappingLockDelay);
	private readonly HashSet<string> _guidedIgnoredSourceKeys = new(StringComparer.Ordinal);
	private readonly Queue<DateTimeOffset> _reportTimes = new();
	private AxisCalibrationCapture? _calibrationCapture;
	private DateTimeOffset _nextRawReportUiUpdate = DateTimeOffset.MinValue;
	private DateTimeOffset _lastReportTextUpdate = DateTimeOffset.MinValue;
	private DateTimeOffset _lastCaptureStatusUpdate = DateTimeOffset.MinValue;
	private DateTimeOffset _captureStatusHoldUntil = DateTimeOffset.MinValue;
	private bool _rawReportDispatchScheduled;
	private bool _guidedMappingActive;
	private bool _guidedArming;
	private bool _guidedWaitingForNeutral;
	private bool _guidedReadyPromptShown;
	private bool _guidedLockCheckScheduled;
	private int _guidedTargetIndex;
	private DateTimeOffset _guidedArmUntil;
	private DateTimeOffset _guidedAcceptInputAt;
	private bool _calibrationActive;
	private bool _disposed;

	public MainWindow()
	{
		_host = new ControllerDiagnosticsHost(new HidSharpControllerProviderOptions());
		Title = "CopperPad";
		Width = 1180;
		Height = 760;
		MinWidth = 980;
		MinHeight = 640;
		Content = BuildContent();

		_host.DevicesChanged += (_, args) => PostUi("Device refresh failed", () => OnDevicesChanged(args));
		_host.RawReportReceived += (_, args) => QueueRawReport(args);
		_host.SnapshotChanged += (_, args) => PostUi("Controller snapshot update failed", () => OnSnapshotChanged(args.Snapshot));
		Opened += async (_, _) => await TryRunUiActionAsync("Startup failed", InitializeAsync).ConfigureAwait(true);
		Closed += (_, _) => Dispose();
	}

	private Control BuildContent()
	{
		var root = new DockPanel { LastChildFill = true };
		DockPanel.SetDock(_statusText, Dock.Bottom);
		_statusText.Margin = new Thickness(12, 6);
		root.Children.Add(_statusText);

		var toolbar = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8,
			Margin = new Thickness(12, 10, 12, 8)
		};
		DockPanel.SetDock(toolbar, Dock.Top);
		toolbar.Children.Add(new TextBlock
		{
			Text = "CopperPad",
			FontSize = 20,
			FontWeight = FontWeight.SemiBold,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0, 0, 16, 0)
		});
		toolbar.Children.Add(Button("Refresh", (_, _) => RefreshDevices()));
		toolbar.Children.Add(_showAllDevicesCheck);
		toolbar.Children.Add(_saveProfileButton);
		toolbar.Children.Add(_newOverrideButton);
		toolbar.Children.Add(Button("Import", async (_, _) => await ImportProfilesAsync().ConfigureAwait(true)));
		toolbar.Children.Add(Button("Export", async (_, _) => await ExportProfilesAsync().ConfigureAwait(true)));
		_saveProfileButton.IsVisible = false;
		_newOverrideButton.IsVisible = false;
		_saveProfileButton.Click += async (_, _) => await SaveDraftProfileAsync().ConfigureAwait(true);
		_newOverrideButton.Click += (_, _) => TryRunUiAction("New override failed", CreateNewOverrideDraft);
		_showAllDevicesCheck.PropertyChanged += (_, args) =>
		{
			if (args.Property == ToggleButton.IsCheckedProperty)
			{
				ApplyDeviceFilter();
			}
		};
		root.Children.Add(toolbar);

		var layout = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("300,*"),
			RowDefinitions = new RowDefinitions("*"),
			Margin = new Thickness(12, 0, 12, 8)
		};
		layout.Children.Add(BuildDevicePane());
		var workspace = BuildWorkspace();
		Grid.SetColumn(workspace, 1);
		layout.Children.Add(workspace);
		root.Children.Add(layout);
		return root;
	}

	private Control BuildDevicePane()
	{
		var panel = new Grid
		{
			RowDefinitions = new RowDefinitions("Auto,Auto,*"),
			ColumnDefinitions = new ColumnDefinitions("*"),
			Margin = new Thickness(0, 0, 12, 0)
		};
		panel.Children.Add(new TextBlock
		{
			Text = "Devices",
			FontSize = 16,
			FontWeight = FontWeight.SemiBold,
			Margin = new Thickness(0, 0, 0, 8)
		});
		_deviceFilterText.Margin = new Thickness(0, 0, 0, 8);
		Grid.SetRow(_deviceFilterText, 1);
		panel.Children.Add(_deviceFilterText);
		_deviceList.SelectionChanged += (_, _) =>
		{
			if (_deviceList.SelectedItem is DeviceListItem item)
			{
				SelectDevice(item.Device);
			}
		};
		Grid.SetRow(_deviceList, 2);
		panel.Children.Add(_deviceList);
		return panel;
	}

	private Control BuildWorkspace()
	{
		_controllerActions.Children.Add(_testProfileButton);
		_controllerActions.Children.Add(_createProfileButton);
		_controllerActions.Children.Add(_editProfileButton);
		_testProfileButton.Click += (_, _) => TryRunUiAction("Open test failed", OpenTestWorkspace);
		_createProfileButton.Click += (_, _) => TryRunUiAction("Create profile failed", OpenCreateProfileWorkspace);
		_editProfileButton.Click += (_, _) => TryRunUiAction("Edit profile failed", OpenEditProfileWorkspace);
		_controllerSummary.Children.Add(_controllerSummaryText);
		_controllerSummary.Children.Add(_controllerActions);

		_tabs.ItemsSource = new object[]
		{
			new TabItem { Header = "Test", Content = BuildTestTab() },
			new TabItem { Header = "Map", Content = BuildMapTab() },
			new TabItem { Header = "Calibrate", Content = BuildCalibrationTab() },
			new TabItem { Header = "Reports", Content = BuildReportsTab() }
		};
		_tabs.IsVisible = false;

		var root = new Grid
		{
			RowDefinitions = new RowDefinitions("*"),
			ColumnDefinitions = new ColumnDefinitions("*")
		};
		root.Children.Add(_controllerSummary);
		root.Children.Add(_tabs);
		return root;
	}

	private Control BuildTabs()
		=> new TabControl
		{
			ItemsSource = new object[]
			{
				new TabItem { Header = "Test", Content = BuildTestTab() },
				new TabItem { Header = "Map", Content = BuildMapTab() },
				new TabItem { Header = "Calibrate", Content = BuildCalibrationTab() },
				new TabItem { Header = "Reports", Content = BuildReportsTab() }
			}
		};

	private Control BuildTestTab()
	{
		var root = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*,*"),
			RowDefinitions = new RowDefinitions("Auto,Auto,*"),
			Margin = new Thickness(8)
		};
		var triggerPanel = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*,*"),
			Margin = new Thickness(0, 0, 0, 16)
		};
		triggerPanel.Children.Add(LabeledControl("Left trigger", _leftTrigger));
		var rightTrigger = LabeledControl("Right trigger", _rightTrigger);
		Grid.SetColumn(rightTrigger, 1);
		triggerPanel.Children.Add(rightTrigger);
		Grid.SetColumnSpan(triggerPanel, 2);
		root.Children.Add(triggerPanel);

		var leftStickPanel = LabeledControl("Left stick", _leftStick);
		Grid.SetRow(leftStickPanel, 1);
		root.Children.Add(leftStickPanel);
		var rightStickPanel = LabeledControl("Right stick", _rightStick);
		Grid.SetColumn(rightStickPanel, 1);
		Grid.SetRow(rightStickPanel, 1);
		root.Children.Add(rightStickPanel);

		var buttons = new WrapPanel
		{
			Margin = new Thickness(0, 18, 0, 0),
			HorizontalAlignment = HorizontalAlignment.Stretch
		};
		foreach (var control in new[]
		{
			ControllerElement.DPadUp,
			ControllerElement.DPadDown,
			ControllerElement.DPadLeft,
			ControllerElement.DPadRight,
			ControllerElement.A,
			ControllerElement.B,
			ControllerElement.X,
			ControllerElement.Y,
			ControllerElement.LeftShoulder,
			ControllerElement.RightShoulder,
			ControllerElement.Select,
			ControllerElement.Start,
			ControllerElement.Menu,
			ControllerElement.LeftStickButton,
			ControllerElement.RightStickButton
		})
		{
			buttons.Children.Add(CreateIndicator(control));
		}

		Grid.SetRow(buttons, 2);
		Grid.SetColumnSpan(buttons, 2);
		root.Children.Add(buttons);
		_stateText.Margin = new Thickness(0, 12, 0, 0);
		buttons.Children.Add(_stateText);
		return root;
	}

	private Control BuildMapTab()
	{
		_targetBox.ItemsSource = MappingTargetItem.All;
		_targetBox.SelectedIndex = 0;
		_targetBox.SelectionChanged += (_, _) => PopulateFieldsFromSelectedTarget();
		_sourceKindBox.ItemsSource = Enum.GetValues<ControllerBindingSourceKind>();
		_sourceKindBox.SelectedItem = ControllerBindingSourceKind.ReportBit;
		_sourceKindBox.SelectionChanged += (_, _) => UpdateSourceFieldAvailability();
		_bindingsList.SelectionChanged += (_, _) =>
		{
			if (_bindingsList.SelectedItem is BindingListItem item)
			{
				SetBindingFields(item.Binding);
			}
		};
		_useSuggestionButton.Click += (_, _) =>
		{
			if (_suggestedSource != null)
			{
				SetSourceFields(_suggestedSource);
				SetCaptureStatus("Suggestion copied to fields. Click Add / Update to store the binding.", ExplicitCaptureStatusHold);
			}
		};
		_ignoreSuggestionButton.Click += (_, _) => IgnoreSuggestedSource();
		_startGuidedMappingButton.Click += (_, _) => StartGuidedMapping();
		_skipGuidedMappingButton.Click += (_, _) => SkipGuidedTarget();
		_stopGuidedMappingButton.Click += (_, _) => StopGuidedMapping("Guided mapping stopped.");

		var guidedPanel = new StackPanel
		{
			Spacing = 8,
			Margin = new Thickness(0, 0, 0, 12)
		};
		_guidedPromptText.FontSize = 18;
		_guidedPromptText.FontWeight = FontWeight.SemiBold;
		_guidedPromptText.Margin = new Thickness(10);
		_guidedPromptText.Text = "Guided mapping asks for one control at a time and locks the detected input automatically.";
		guidedPanel.Children.Add(new Border
		{
			Background = new SolidColorBrush(Color.FromRgb(36, 48, 54)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(84, 150, 168)),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(4),
			Child = _guidedPromptText
		});
		var guidedActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
		guidedActions.Children.Add(_startGuidedMappingButton);
		guidedActions.Children.Add(_skipGuidedMappingButton);
		guidedActions.Children.Add(_stopGuidedMappingButton);
		guidedPanel.Children.Add(guidedActions);

		var editor = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,220,Auto,220"),
			RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
			Margin = new Thickness(0, 0, 0, 12),
			ColumnSpacing = 12,
			RowSpacing = 8
		};
		AddFormRow(editor, 0, "Target", _targetBox, "Kind", _sourceKindBox);
		AddFormRow(editor, 1, "Offset", _offsetBox, "Bit", _bitBox);
		AddFormRow(editor, 2, "Hat", _hatBox, "", new StackPanel());
		AddFormRow(editor, 3, "Options", _sourceInvertCheck, "", new StackPanel());
		var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
		actions.Children.Add(Button("Capture Baseline", (_, _) => CaptureBaseline()));
		actions.Children.Add(_useSuggestionButton);
		actions.Children.Add(_ignoreSuggestionButton);
		actions.Children.Add(Button("Add / Update", (_, _) => AddOrUpdateBinding()));
		actions.Children.Add(Button("Remove", (_, _) => RemoveSelectedBinding()));
		actions.Children.Add(Button("Clear Bindings", (_, _) => ClearBindings()));
		Grid.SetRow(actions, 4);
		Grid.SetColumnSpan(actions, 4);
		editor.Children.Add(actions);

		var root = new Grid
		{
			RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
			Margin = new Thickness(8)
		};
		root.Children.Add(guidedPanel);
		Grid.SetRow(editor, 1);
		root.Children.Add(editor);
		var bindingsScroll = new ScrollViewer
		{
			Content = _bindingsList,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
		};
		Grid.SetRow(bindingsScroll, 2);
		root.Children.Add(bindingsScroll);
		_captureStatusText.Margin = new Thickness(0, 10, 0, 0);
		Grid.SetRow(_captureStatusText, 3);
		root.Children.Add(_captureStatusText);
		_validationText.Margin = new Thickness(0, 8, 0, 0);
		Grid.SetRow(_validationText, 4);
		root.Children.Add(_validationText);
		UpdateSourceFieldAvailability();
		return root;
	}

	private Control BuildCalibrationTab()
	{
		_calibrationTargetBox.ItemsSource = AxisTargets;
		_calibrationTargetBox.SelectedItem = ControllerElement.LeftStickX;
		_calibrationTargetBox.SelectionChanged += (_, _) => LoadCalibrationFromTarget();
		_deadzoneSlider.PropertyChanged += (_, args) =>
		{
			if (args.Property == RangeBase.ValueProperty)
			{
				UpdateCalibrationPreview();
			}
		};
		_saturationSlider.PropertyChanged += (_, args) =>
		{
			if (args.Property == RangeBase.ValueProperty)
			{
				UpdateCalibrationPreview();
			}
		};

		var root = new StackPanel
		{
			Spacing = 12,
			Margin = new Thickness(8)
		};
		root.Children.Add(LabeledControl("Axis / trigger", _calibrationTargetBox));
		var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
		buttons.Children.Add(Button("Start Range", (_, _) => StartCalibrationCapture()));
		buttons.Children.Add(Button("Stop Range", (_, _) => StopCalibrationCapture()));
		buttons.Children.Add(Button("Set Center", (_, _) => CaptureCalibrationCenter()));
		buttons.Children.Add(Button("Apply", (_, _) => ApplyCalibration()));
		root.Children.Add(buttons);
		root.Children.Add(_invertCheck);
		root.Children.Add(LabeledControl("Deadzone", _deadzoneSlider));
		root.Children.Add(LabeledControl("Saturation", _saturationSlider));
		root.Children.Add(_calibrationStatusText);
		root.Children.Add(_calibrationPreviewText);
		return root;
	}

	private Control BuildReportsTab()
	{
		var root = new Grid
		{
			RowDefinitions = new RowDefinitions("Auto,Auto,*,*"),
			Margin = new Thickness(8),
			RowSpacing = 8
		};
		root.Children.Add(_reportRateText);
		Grid.SetRow(_changedBytesText, 1);
		root.Children.Add(_changedBytesText);
		var rawScroll = new ScrollViewer { Content = _rawHexText };
		Grid.SetRow(rawScroll, 2);
		root.Children.Add(rawScroll);
		var descriptorScroll = new ScrollViewer { Content = _descriptorText };
		Grid.SetRow(descriptorScroll, 3);
		root.Children.Add(descriptorScroll);
		return root;
	}

	private async Task InitializeAsync()
	{
		try
		{
			_profiles = await _profileStore.LoadAsync().ConfigureAwait(true);
			_host.UpdateProfiles(_profiles);
			SetStatus($"Profiles: {_profileStore.Path}");
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
		{
			_profiles = ControllerProfileSet.Empty;
			SetStatus("Profile load failed: " + ex.Message);
		}

		StartHost();
	}

	private void RefreshDevices()
	{
		try
		{
			_host.Stop();
			_host.Start();
		}
		catch (Exception ex) when (IsRecoverableHidException(ex))
		{
			SetStatus("Controller refresh failed: " + ex.Message);
		}
	}

	private void OnDevicesChanged(HidDevicesChangedEventArgs args)
	{
		_allDevices = args.Devices;
		if (!string.IsNullOrWhiteSpace(args.Diagnostic))
		{
			SetStatus(args.Diagnostic);
		}

		ApplyDeviceFilter();
	}

	private void ApplyDeviceFilter()
	{
		var selectedId = _selectedDevice?.Id;
		var showAll = _showAllDevicesCheck.IsChecked == true;
		var devices = showAll ? _allDevices : _allDevices.Where(DeviceDisplay.IsLikelyGameController).ToArray();
		var items = devices.Select(device => new DeviceListItem(device)).ToArray();
		_deviceList.ItemsSource = items;
		var hiddenCount = _allDevices.Count - items.Length;
		_deviceFilterText.Text = showAll
			? $"Showing all HID devices: {items.Length}"
			: hiddenCount == 0
				? $"Showing controllers: {items.Length}"
				: $"Showing controllers: {items.Length}   Hidden HID: {hiddenCount}";
		var selected = items.FirstOrDefault(item => string.Equals(item.Device.Id, selectedId, StringComparison.Ordinal)) ??
			items.FirstOrDefault();
		if (selected != null)
		{
			_deviceList.SelectedItem = selected;
			SelectDevice(selected.Device);
		}
		else
		{
			SelectDevice(null);
		}
	}

	private void StartHost()
	{
		try
		{
			_host.Start();
		}
		catch (Exception ex) when (IsRecoverableHidException(ex))
		{
			SetStatus("Controller scan failed: " + ex.Message);
		}
	}

	private void SelectDevice(HidDeviceInfo? device)
	{
		_selectedDevice = device;
		_lastReport = null;
		_previousReport = null;
		_baselineReport = null;
		_guidedReleaseBaselineReport = null;
		_suggestedSource = null;
		_useSuggestionButton.IsEnabled = false;
		_ignoreSuggestionButton.IsEnabled = false;
		_guidedReleaseSource = null;
		_reportTimes.Clear();
		_lastReportTextUpdate = DateTimeOffset.MinValue;
		_lastCaptureStatusUpdate = DateTimeOffset.MinValue;
		_captureStatusHoldUntil = DateTimeOffset.MinValue;
		_guidedMappingActive = false;
		_guidedArming = false;
		_guidedWaitingForNeutral = false;
		_guidedReadyPromptShown = false;
		_guidedLockCheckScheduled = false;
		_guidedCapture.Reset();
		_guidedIgnoredSourceKeys.Clear();
		_guidedReleaseSource = null;
		_guidedReleaseBaselineReport = null;
		UpdateGuidedButtons();
		_host.SelectDevice(device?.Id);
		if (device == null)
		{
			_descriptorText.Text = "";
			_controllerSummaryText.Text = "Select a controller.";
			_draftProfile = null;
			_saveProfileButton.Content = "Save Profile";
			_saveProfileButton.IsVisible = false;
			_newOverrideButton.IsEnabled = false;
			_newOverrideButton.IsVisible = false;
			_testProfileButton.IsEnabled = false;
			_createProfileButton.IsEnabled = false;
			_editProfileButton.IsEnabled = false;
			ShowSummaryWorkspace();
			SetStatus("No controller selected.");
		}
		else
		{
			_draftProfile = FindSavedProfile(device) ?? ProfileEditor.CreateDefaultProfile(device, DateTimeOffset.UtcNow);
			_newOverrideButton.IsEnabled = true;
			UpdateSelectedDeviceDetails();
			ShowSummaryWorkspace();
			SetStatus("Selected " + device.ProductName);
		}

		UpdateBindingList();
		UpdateValidation();
		LoadCalibrationFromTarget();
		UpdateCaptureStatus();
	}

	private void QueueRawReport(ControllerRawReportReceivedEventArgs args)
	{
		TimeSpan delay;
		lock (_rawReportGate)
		{
			_pendingRawReport = new PendingRawReport(args.Device, args.Report.ToArray(), args.Timestamp);
			if (_rawReportDispatchScheduled)
			{
				return;
			}

			_rawReportDispatchScheduled = true;
			var now = DateTimeOffset.UtcNow;
			delay = _nextRawReportUiUpdate > now ? _nextRawReportUiUpdate - now : TimeSpan.Zero;
		}

		Dispatcher.UIThread.Post(() => DispatcherTimer.RunOnce(ProcessPendingRawReport, delay));
	}

	private void ProcessPendingRawReport()
	{
		PendingRawReport? pending;
		lock (_rawReportGate)
		{
			pending = _pendingRawReport;
			_pendingRawReport = null;
			_rawReportDispatchScheduled = false;
			_nextRawReportUiUpdate = DateTimeOffset.UtcNow + RawReportUiInterval;
		}

		if (pending != null)
		{
			TryRunUiAction("Raw report update failed", () => OnRawReport(pending));
		}
	}

	private void OnRawReport(PendingRawReport pending)
	{
		if (_selectedDevice == null || !string.Equals(pending.Device.Id, _selectedDevice.Id, StringComparison.Ordinal))
		{
			return;
		}

		_previousReport = _lastReport;
		_lastReport = pending.Report;
		_reportTimes.Enqueue(pending.Timestamp);
		while (_reportTimes.Count > 0 && pending.Timestamp - _reportTimes.Peek() > TimeSpan.FromSeconds(1))
		{
			_reportTimes.Dequeue();
		}

		if (_lastReportTextUpdate == DateTimeOffset.MinValue ||
			pending.Timestamp - _lastReportTextUpdate >= ReportTextUpdateInterval)
		{
			_lastReportTextUpdate = pending.Timestamp;
			_rawHexText.Text = ToHexRows(_lastReport);
			_reportRateText.Text = $"Reports: {_reportTimes.Count}/s   Last: {pending.Timestamp:HH:mm:ss.fff}";
			var changes = ReportAnalyzer.DetectChanges(_previousReport, _lastReport);
			_changedBytesText.Text = changes.Count == 0
				? "Changed bytes: none"
				: "Changed bytes: " + string.Join(", ", changes.Take(10).Select(change => $"{change.Offset}: {change.Baseline:X2}->{change.Current:X2}"));
		}

		if (_baselineReport != null)
		{
			var baselineChanges = ReportAnalyzer.DetectChanges(_baselineReport, _lastReport);
			var neutralChanges = _previousReport == null
				? Array.Empty<ReportChange>()
				: ReportAnalyzer.DetectChanges(_previousReport, _lastReport);
			UpdateGuidedMapping(baselineChanges, neutralChanges, pending.Timestamp);
			var selectedTarget = GetSelectedTarget();
			var best = SelectBestSuggestedChange(selectedTarget, baselineChanges);
			if (best != null)
			{
				_suggestedSource = best.SuggestedSource;
				_useSuggestionButton.IsEnabled = true;
				_ignoreSuggestionButton.IsEnabled = true;
			}
			else
			{
				_suggestedSource = null;
				_useSuggestionButton.IsEnabled = false;
				_ignoreSuggestionButton.IsEnabled = false;
			}

			UpdateCaptureStatus(force: false);
		}

		ObserveCalibration(_lastReport);
	}

	private void OnSnapshotChanged(CopperControllerSnapshot state)
	{
		if (_selectedDevice == null || !string.Equals(state.ControllerId, _selectedDevice.Id, StringComparison.Ordinal))
		{
			return;
		}

		var leftX = state.GetAxis(ControllerElement.LeftStickX);
		var leftY = state.GetAxis(ControllerElement.LeftStickY);
		var rightX = state.GetAxis(ControllerElement.RightStickX);
		var rightY = state.GetAxis(ControllerElement.RightStickY);
		var leftTrigger = state.GetAxis(ControllerElement.LeftTrigger);
		var rightTrigger = state.GetAxis(ControllerElement.RightTrigger);
		_leftStick.SetPosition(leftX, leftY);
		_rightStick.SetPosition(rightX, rightY);
		_leftTrigger.Value = leftTrigger;
		_rightTrigger.Value = rightTrigger;
		SetIndicator(ControllerElement.A, state.IsPressed(ControllerElement.A));
		SetIndicator(ControllerElement.B, state.IsPressed(ControllerElement.B));
		SetIndicator(ControllerElement.X, state.IsPressed(ControllerElement.X));
		SetIndicator(ControllerElement.Y, state.IsPressed(ControllerElement.Y));
		SetIndicator(ControllerElement.LeftShoulder, state.IsPressed(ControllerElement.LeftShoulder));
		SetIndicator(ControllerElement.RightShoulder, state.IsPressed(ControllerElement.RightShoulder));
		SetIndicator(ControllerElement.Select, state.IsPressed(ControllerElement.Select));
		SetIndicator(ControllerElement.Start, state.IsPressed(ControllerElement.Start));
		SetIndicator(ControllerElement.Menu, state.IsPressed(ControllerElement.Menu));
		SetIndicator(ControllerElement.LeftStickButton, state.IsPressed(ControllerElement.LeftStickButton));
		SetIndicator(ControllerElement.RightStickButton, state.IsPressed(ControllerElement.RightStickButton));
		SetIndicator(ControllerElement.DPadUp, state.IsPressed(ControllerElement.DPadUp));
		SetIndicator(ControllerElement.DPadDown, state.IsPressed(ControllerElement.DPadDown));
		SetIndicator(ControllerElement.DPadLeft, state.IsPressed(ControllerElement.DPadLeft));
		SetIndicator(ControllerElement.DPadRight, state.IsPressed(ControllerElement.DPadRight));
		_stateText.Text =
			$"LX {leftX:0.00}  LY {leftY:0.00}  RX {rightX:0.00}  RY {rightY:0.00}  LT {leftTrigger:0.00}  RT {rightTrigger:0.00}";
		if (!string.IsNullOrWhiteSpace(state.Diagnostic))
		{
			SetStatus(state.Diagnostic);
		}
	}

	private void CaptureBaseline()
	{
		_baselineReport = _lastReport?.ToArray();
		_suggestedSource = null;
		_useSuggestionButton.IsEnabled = false;
		_ignoreSuggestionButton.IsEnabled = false;
		if (_baselineReport == null)
		{
			SetCaptureStatus("No report received yet. Move or press the controller once, then capture baseline again.", ExplicitCaptureStatusHold);
			return;
		}

		SetCaptureStatus($"Baseline captured: {_baselineReport.Length} bytes. Press or move one physical control.", ExplicitCaptureStatusHold);
	}

	private void AddOrUpdateBinding()
	{
		if (_draftProfile == null)
		{
			return;
		}

		var target = GetSelectedTarget();
		var existingAxis = _draftProfile.Bindings.FirstOrDefault(binding => binding.Target == target)?.Axis;
		var binding = ProfileEditor.CreateBinding(target, GetSourceFromFields(), existingAxis);
		_draftProfile = ProfileEditor.UpsertBinding(_draftProfile, binding);
		UpdateBindingList();
		UpdateValidation();
		LoadCalibrationFromTarget();
		SetCaptureStatus($"Added / updated {target}: {ReportAnalyzer.FormatSource(binding.Source)}", ExplicitCaptureStatusHold);
	}

	private void StartGuidedMapping()
	{
		if (_draftProfile == null)
		{
			SetCaptureStatus("Create a profile before guided mapping.", ExplicitCaptureStatusHold);
			return;
		}

		if (_lastReport == null)
		{
			SetCaptureStatus("No neutral report yet. Release the controller controls and wait for input reports.", ExplicitCaptureStatusHold);
			return;
		}

		var firstMissing = ProfileEditor.MappableTargets
			.Select((target, index) => new { target, index })
			.FirstOrDefault(item => !_draftProfile.Bindings.Any(binding => binding.Target == item.target));
		_guidedTargetIndex = firstMissing?.index ?? 0;
		_guidedMappingActive = true;
		_guidedIgnoredSourceKeys.Clear();
		_suggestedSource = null;
		_useSuggestionButton.IsEnabled = false;
		_ignoreSuggestionButton.IsEnabled = false;
		UpdateGuidedButtons();
		BeginGuidedArming();
	}

	private void IgnoreSuggestedSource()
	{
		if (_suggestedSource == null)
		{
			return;
		}

		var ignored = ReportAnalyzer.FormatSource(_suggestedSource);
		AddGuidedIgnoredSource(_suggestedSource);
		_suggestedSource = null;
		_useSuggestionButton.IsEnabled = false;
		_ignoreSuggestionButton.IsEnabled = false;
		_guidedCapture.Reset();
		SetCaptureStatus("Ignoring " + ignored + " for this mapping session.", ExplicitCaptureStatusHold);
	}

	private void SkipGuidedTarget()
	{
		if (!_guidedMappingActive)
		{
			return;
		}

		if (_guidedWaitingForNeutral)
		{
			AdvanceGuidedTarget();
			return;
		}

		AdvanceGuidedTarget();
	}

	private void StopGuidedMapping(string message)
	{
		_guidedMappingActive = false;
		_guidedArming = false;
		_guidedWaitingForNeutral = false;
		_guidedReadyPromptShown = false;
		_guidedLockCheckScheduled = false;
		_guidedCapture.Reset();
		_guidedIgnoredSourceKeys.Clear();
		_guidedReleaseSource = null;
		_guidedReleaseBaselineReport = null;
		_suggestedSource = null;
		_useSuggestionButton.IsEnabled = false;
		_ignoreSuggestionButton.IsEnabled = false;
		UpdateGuidedButtons();
		_guidedPromptText.Text = "Guided mapping asks for one control at a time and locks the detected input automatically.";
		SetCaptureStatus(message, ExplicitCaptureStatusHold);
	}

	private void UpdateGuidedMapping(
		IReadOnlyList<ReportChange> changes,
		IReadOnlyList<ReportChange> neutralChanges,
		DateTimeOffset timestamp)
	{
		if (!_guidedMappingActive || _baselineReport == null || _lastReport == null)
		{
			return;
		}

		if (_guidedArming)
		{
			AddGuidedIgnoredChanges(changes);
			AddGuidedIgnoredChanges(neutralChanges);

			if (timestamp >= _guidedArmUntil)
			{
				_guidedArming = false;
				_guidedReadyPromptShown = false;
				_guidedAcceptInputAt = timestamp + GuidedMappingReadyDelay;
				_guidedCapture.Reset();
				ShowGuidedPrompt();
			}

			return;
		}

		if (_guidedWaitingForNeutral)
		{
			if (_guidedReleaseSource != null &&
				_guidedReleaseBaselineReport != null &&
				GuidedMappingCapture.IsSourceReleased(_guidedReleaseSource, _guidedReleaseBaselineReport, _lastReport))
			{
				AdvanceGuidedTarget();
			}

			return;
		}

		if (timestamp < _guidedAcceptInputAt)
		{
			AddGuidedIgnoredChanges(changes);
			AddGuidedIgnoredChanges(neutralChanges);

			return;
		}

		if (!_guidedReadyPromptShown)
		{
			AddGuidedIgnoredChanges(changes);
			AddGuidedIgnoredChanges(neutralChanges);

			_baselineReport = _lastReport.ToArray();
			_guidedCapture.Reset();
			_guidedReadyPromptShown = true;
			ShowGuidedPrompt();
			return;
		}

		var target = ProfileEditor.MappableTargets[_guidedTargetIndex];
		var candidate = SelectGuidedCandidate(target, changes);
		if (!_guidedCapture.Observe(target, candidate, _baselineReport, _lastReport, timestamp))
		{
			if (_guidedCapture.CandidateSource != null)
			{
				ScheduleGuidedLockCheck();
			}

			return;
		}

		CommitGuidedLockedSource();
	}

	private ReportChange? SelectGuidedCandidate(ControllerElement target, IReadOnlyList<ReportChange> changes)
	{
		if (_baselineReport == null || _lastReport == null)
		{
			return null;
		}

		var bestScore = -1;
		ReportChange? best = null;
		foreach (var change in changes)
		{
			var normalized = GuidedMappingCapture.NormalizeForTarget(target, change, _baselineReport, _lastReport);
			var source = normalized.SuggestedSource;
			if (IsGuidedSourceIgnored(source) ||
				IsGuidedSourceAlreadyBound(target, source))
			{
				continue;
			}

			var score = GuidedMappingCapture.GetCandidateScore(target, normalized, _baselineReport, _lastReport) +
				ProfileEditor.GetSourcePreferenceScore(_draftProfile, target, source);
			if (score > bestScore)
			{
				bestScore = score;
				best = normalized;
			}
		}

		return best;
	}

	private ReportChange? SelectBestSuggestedChange(ControllerElement target, IReadOnlyList<ReportChange> changes)
	{
		if (_baselineReport == null || _lastReport == null)
		{
			return null;
		}

		var bestScore = int.MinValue;
		ReportChange? best = null;
		foreach (var change in changes)
		{
			var normalized = GuidedMappingCapture.NormalizeForTarget(target, change, _baselineReport, _lastReport);
			var source = normalized.SuggestedSource;
			if (IsGuidedSourceIgnored(source))
			{
				continue;
			}

			var score = GuidedMappingCapture.GetCandidateScore(target, normalized, _baselineReport, _lastReport) +
				ProfileEditor.GetSourcePreferenceScore(_draftProfile, target, source);
			if (score > bestScore)
			{
				bestScore = score;
				best = normalized;
			}
		}

		return bestScore >= 0 ? best : null;
	}

	private void AddGuidedIgnoredChanges(IReadOnlyList<ReportChange> changes)
	{
		foreach (var change in changes)
		{
			AddGuidedIgnoredSource(change.SuggestedSource);
		}
	}

	private void AddGuidedIgnoredSource(ControllerBindingSource source)
	{
		_guidedIgnoredSourceKeys.Add(ProfileEditor.GetSourceKey(source));
		if (source.Kind == ControllerBindingSourceKind.ReportBit)
		{
			_guidedIgnoredSourceKeys.Add(ProfileEditor.GetSourceKey(source with { Invert = !source.Invert }));
		}
	}

	private bool IsGuidedSourceIgnored(ControllerBindingSource source)
	{
		if (_guidedIgnoredSourceKeys.Contains(ProfileEditor.GetSourceKey(source)))
		{
			return true;
		}

		return source.Kind == ControllerBindingSourceKind.ReportBit &&
			_guidedIgnoredSourceKeys.Contains(ProfileEditor.GetSourceKey(source with { Invert = !source.Invert }));
	}

	private void ScheduleGuidedLockCheck()
	{
		if (_guidedLockCheckScheduled)
		{
			return;
		}

		_guidedLockCheckScheduled = true;
		DispatcherTimer.RunOnce(() =>
			TryRunUiAction("Guided mapping lock failed", () =>
			{
				_guidedLockCheckScheduled = false;
				if (!_guidedMappingActive ||
					_guidedArming ||
					_guidedWaitingForNeutral ||
					_baselineReport == null ||
					_lastReport == null)
				{
					return;
				}

				if (_guidedCapture.TryLockCandidate(_baselineReport, _lastReport, DateTimeOffset.UtcNow))
				{
					CommitGuidedLockedSource();
				}
			}),
			GuidedMappingLockDelay);
	}

	private void CommitGuidedLockedSource()
	{
		if (_guidedCapture.LockedSource == null || _baselineReport == null)
		{
			return;
		}

		_guidedLockCheckScheduled = false;
		var lockedSource = _guidedCapture.LockedSource;
		_guidedReleaseSource = lockedSource;
		_guidedReleaseBaselineReport = _baselineReport.ToArray();
		ApplyGuidedBinding(lockedSource);
		_guidedWaitingForNeutral = true;
		_guidedCapture.Reset();
		UpdateGuidedButtons();
		ShowGuidedPrompt();
	}

	private void ApplyGuidedBinding(ControllerBindingSource source)
	{
		if (_draftProfile == null)
		{
			return;
		}

		var target = ProfileEditor.MappableTargets[_guidedTargetIndex];
		var existingAxis = _draftProfile.Bindings.FirstOrDefault(binding => binding.Target == target)?.Axis;
		var binding = ProfileEditor.CreateBinding(target, source, existingAxis);
		_draftProfile = ProfileEditor.UpsertBinding(_draftProfile, binding);
		SetSelectedTarget(target);
		SetSourceFields(source);
		UpdateBindingList();
		UpdateValidation();
		LoadCalibrationFromTarget();
	}

	private void AdvanceGuidedTarget()
	{
		_guidedTargetIndex++;
		_suggestedSource = null;
		_guidedReleaseSource = null;
		_guidedReleaseBaselineReport = null;
		_useSuggestionButton.IsEnabled = false;
		_ignoreSuggestionButton.IsEnabled = false;
		if (_guidedTargetIndex >= ProfileEditor.MappableTargets.Count)
		{
			StopGuidedMapping("Guided mapping complete. Save the profile, then test controls.");
			return;
		}

		BeginGuidedArming();
	}

	private void BeginGuidedArming()
	{
		if (_lastReport == null)
		{
			StopGuidedMapping("No controller reports are available.");
			return;
		}

		_baselineReport = _lastReport.ToArray();
		_guidedArming = true;
		_guidedWaitingForNeutral = false;
		_guidedLockCheckScheduled = false;
		_guidedReleaseSource = null;
		_guidedReleaseBaselineReport = null;
		_guidedCapture.Reset();
		_guidedArmUntil = DateTimeOffset.UtcNow + GuidedMappingArmDelay;
		UpdateGuidedButtons();
		_guidedPromptText.Text = "Release all controls. Measuring neutral input...";
		SetCaptureStatus("Measuring neutral input before the next action.", ExplicitCaptureStatusHold);
	}

	private void ShowGuidedPrompt()
	{
		if (!_guidedMappingActive)
		{
			return;
		}

		var target = ProfileEditor.MappableTargets[_guidedTargetIndex];
		SetSelectedTarget(target);
		if (_guidedArming)
		{
			_guidedPromptText.Text = "Release all controls. Measuring neutral input...";
			return;
		}

		if (_guidedWaitingForNeutral)
		{
			_guidedPromptText.Text = $"Locked {MappingTargetItem.All[_guidedTargetIndex]}. Release controls, or click Continue if the controller stays active.";
			SetCaptureStatus($"Locked {target}. Release controls or click Continue.", ExplicitCaptureStatusHold);
			return;
		}

		if (!_guidedReadyPromptShown)
		{
			_guidedPromptText.Text = $"Get ready: {GetGuidedActionText(target)}. Wait for the next prompt.";
			SetCaptureStatus("Ignoring early changes while you get ready.", ExplicitCaptureStatusHold);
			return;
		}

		_guidedPromptText.Text = $"Do this now: {GetGuidedActionText(target)}";
		SetCaptureStatus($"Waiting for {MappingTargetItem.All[_guidedTargetIndex]}...", ExplicitCaptureStatusHold);
	}

	private void UpdateGuidedButtons()
	{
		_startGuidedMappingButton.IsEnabled = !_guidedMappingActive;
		_skipGuidedMappingButton.IsEnabled = _guidedMappingActive;
		_skipGuidedMappingButton.Content = _guidedWaitingForNeutral ? "Continue" : "Skip";
		_stopGuidedMappingButton.IsEnabled = _guidedMappingActive;
	}

	private bool IsGuidedSourceAlreadyBound(ControllerElement target, ControllerBindingSource source)
	{
		if (_draftProfile == null)
		{
			return false;
		}

		var sourceKey = ProfileEditor.GetSourceKey(source);
		return _draftProfile.Bindings.Any(binding =>
			binding.Target != target &&
			string.Equals(ProfileEditor.GetSourceKey(binding.Source), sourceKey, StringComparison.Ordinal));
	}

	private static string GetGuidedActionText(ControllerElement target)
		=> target switch
		{
			ControllerElement.South => "press the bottom face button (South / A)",
			ControllerElement.East => "press the right face button (East / B)",
			ControllerElement.West => "press the left face button (West / X)",
			ControllerElement.North => "press the top face button (North / Y)",
			ControllerElement.DPadUp => "press D-pad up",
			ControllerElement.DPadDown => "press D-pad down",
			ControllerElement.DPadLeft => "press D-pad left",
			ControllerElement.DPadRight => "press D-pad right",
			ControllerElement.LeftShoulder => "press the left shoulder button",
			ControllerElement.RightShoulder => "press the right shoulder button",
			ControllerElement.Select => "press Select / Back / View",
			ControllerElement.Start => "press Start / Options",
			ControllerElement.Menu => "press Menu / Guide / Home",
			ControllerElement.LeftStickButton => "press the left stick button",
			ControllerElement.RightStickButton => "press the right stick button",
			ControllerElement.LeftStickX => "move the left stick left or right",
			ControllerElement.LeftStickY => "move the left stick up or down",
			ControllerElement.RightStickX => "move the right stick left or right",
			ControllerElement.RightStickY => "move the right stick up or down",
			ControllerElement.LeftTrigger => "pull the left trigger",
			ControllerElement.RightTrigger => "pull the right trigger",
			_ => "press or move " + target
		};

	private void RemoveSelectedBinding()
	{
		if (_draftProfile == null)
		{
			return;
		}

		_draftProfile = ProfileEditor.RemoveBinding(_draftProfile, GetSelectedTarget());
		UpdateBindingList();
		UpdateValidation();
		LoadCalibrationFromTarget();
		SetCaptureStatus($"Removed {GetSelectedTarget()} binding.", ExplicitCaptureStatusHold);
	}

	private void ClearBindings()
	{
		if (_draftProfile == null)
		{
			return;
		}

		_draftProfile = _draftProfile with { Bindings = [] };
		_guidedIgnoredSourceKeys.Clear();
		_guidedCapture.Reset();
		_suggestedSource = null;
		_useSuggestionButton.IsEnabled = false;
		_ignoreSuggestionButton.IsEnabled = false;
		UpdateBindingList();
		UpdateValidation();
		LoadCalibrationFromTarget();
		SetCaptureStatus("Cleared all bindings. Start guided mapping again.", ExplicitCaptureStatusHold);
	}

	private void CreateNewOverrideDraft()
	{
		if (_selectedDevice == null)
		{
			SetStatus("No controller selected.");
			return;
		}

		_draftProfile = ProfileEditor.CreateDefaultProfile(_selectedDevice, DateTimeOffset.UtcNow);
		UpdateBindingList();
		UpdateValidation();
		LoadCalibrationFromTarget();
		UpdateSelectedDeviceDetails();
		SetStatus("New override draft for " + _selectedDevice.ProductName);
	}

	private void OpenTestWorkspace()
	{
		if (_selectedDevice == null)
		{
			SetStatus("No controller selected.");
			return;
		}

		if (FindSavedProfile(_selectedDevice) == null)
		{
			SetStatus("Create a profile before testing controls.");
			return;
		}

		ShowTabbedWorkspace(WorkspaceMode.Test, tabIndex: 0);
		SetStatus("Testing " + _selectedDevice.ProductName);
	}

	private void OpenCreateProfileWorkspace()
	{
		if (_selectedDevice == null)
		{
			SetStatus("No controller selected.");
			return;
		}

		_draftProfile = ProfileEditor.CreateDefaultProfile(_selectedDevice, DateTimeOffset.UtcNow);
		UpdateBindingList();
		UpdateValidation();
		LoadCalibrationFromTarget();
		UpdateSelectedDeviceDetails();
		ShowTabbedWorkspace(WorkspaceMode.EditProfile, tabIndex: 1);
		SetStatus("Creating profile for " + _selectedDevice.ProductName);
	}

	private void OpenEditProfileWorkspace()
	{
		if (_selectedDevice == null)
		{
			SetStatus("No controller selected.");
			return;
		}

		_draftProfile = FindSavedProfile(_selectedDevice) ?? ProfileEditor.CreateDefaultProfile(_selectedDevice, DateTimeOffset.UtcNow);
		UpdateBindingList();
		UpdateValidation();
		LoadCalibrationFromTarget();
		UpdateSelectedDeviceDetails();
		ShowTabbedWorkspace(WorkspaceMode.EditProfile, tabIndex: 1);
		SetStatus("Editing profile for " + _selectedDevice.ProductName);
	}

	private async Task SaveDraftProfileAsync()
	{
		if (_draftProfile == null || _selectedDevice == null)
		{
			SetStatus("No profile to save.");
			return;
		}

		var issues = ProfileEditor.ValidateProfile(_draftProfile, _selectedDevice.MaxInputReportLength);
		if (issues.Count > 0)
		{
			SetStatus("Profile has validation errors.");
			UpdateValidation();
			return;
		}

		var now = DateTimeOffset.UtcNow;
		var profile = _draftProfile with
		{
			CreatedAt = _draftProfile.CreatedAt ?? now,
			UpdatedAt = now
		};
		_profiles = ProfileEditor.MergeProfile(_profiles, profile);
		_draftProfile = profile;
		try
		{
			await _profileStore.SaveAsync(_profiles).ConfigureAwait(true);
			_host.UpdateProfiles(_profiles);
			UpdateSelectedDeviceDetails();
			SetStatus("Saved " + profile.Name);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			SetStatus("Profile save failed: " + ex.Message);
		}
	}

	private async Task ImportProfilesAsync()
	{
		var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			AllowMultiple = false,
			Title = "Import CopperPad profiles",
			FileTypeFilter =
			[
				new FilePickerFileType("JSON profiles") { Patterns = ["*.json"] }
			]
		}).ConfigureAwait(true);
		var file = files.FirstOrDefault();
		if (file == null)
		{
			return;
		}

		try
		{
			await using var stream = await file.OpenReadAsync().ConfigureAwait(true);
			_profiles = await JsonControllerProfileSerializer.LoadAsync(stream).ConfigureAwait(true);
			await _profileStore.SaveAsync(_profiles).ConfigureAwait(true);
			_host.UpdateProfiles(_profiles);
			if (_selectedDevice != null)
			{
				SelectDevice(_selectedDevice);
			}

			SetStatus("Imported profiles from " + file.Name);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
		{
			SetStatus("Import failed: " + ex.Message);
		}
	}

	private async Task ExportProfilesAsync()
	{
		var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "Export CopperPad profiles",
			SuggestedFileName = "copperpad-profiles.json",
			DefaultExtension = "json",
			FileTypeChoices =
			[
				new FilePickerFileType("JSON profiles") { Patterns = ["*.json"] }
			]
		}).ConfigureAwait(true);
		if (file == null)
		{
			return;
		}

		try
		{
			await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
			await JsonControllerProfileSerializer.SaveAsync(stream, _profiles).ConfigureAwait(true);
			SetStatus("Exported profiles to " + file.Name);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			SetStatus("Export failed: " + ex.Message);
		}
	}

	private void PopulateFieldsFromSelectedTarget()
	{
		if (_draftProfile == null)
		{
			return;
		}

		var binding = _draftProfile.Bindings.FirstOrDefault(binding => binding.Target == GetSelectedTarget());
		if (binding != null)
		{
			SetBindingFields(binding);
		}

		LoadCalibrationFromTarget();
	}

	private void SetBindingFields(ControllerBinding binding)
	{
		SetSelectedTarget(binding.Target);
		SetSourceFields(binding.Source);
	}

	private void SetSourceFields(ControllerBindingSource source)
	{
		_sourceKindBox.SelectedItem = source.Kind;
		_offsetBox.Value = source.Offset;
		_bitBox.Value = source.Bit;
		_hatBox.Value = source.HatValue ?? 0;
		_sourceInvertCheck.IsChecked = source.Invert;
		UpdateSourceFieldAvailability();
	}

	private ControllerBindingSource GetSourceFromFields()
	{
		var kind = _sourceKindBox.SelectedItem is ControllerBindingSourceKind selectedKind
			? selectedKind
			: ControllerBindingSourceKind.ReportBit;
		return new ControllerBindingSource
		{
			Kind = kind,
			Offset = DecimalToInt(_offsetBox.Value),
			Bit = DecimalToInt(_bitBox.Value),
			HatValue = kind == ControllerBindingSourceKind.Hat ? DecimalToInt(_hatBox.Value) : null,
			Invert = kind == ControllerBindingSourceKind.ReportBit && _sourceInvertCheck.IsChecked == true
		};
	}

	private ControllerElement GetSelectedTarget()
		=> _targetBox.SelectedItem is MappingTargetItem item ? item.Element : ControllerElement.South;

	private void SetSelectedTarget(ControllerElement target)
	{
		var item = MappingTargetItem.All.FirstOrDefault(item => item.Element == target);
		if (item != null)
		{
			_targetBox.SelectedItem = item;
		}
	}

	private void StartCalibrationCapture()
	{
		var binding = GetCalibrationBinding();
		if (binding == null)
		{
			SetCalibrationStatus("Map this control before calibration.");
			return;
		}

		_calibrationCapture = AxisCalibrationCapture.From(binding.Axis);
		_calibrationActive = true;
		if (_lastReport != null)
		{
			_calibrationCapture.Observe(ReportAnalyzer.ReadSourceValue(binding.Source, _lastReport));
		}

		UpdateCalibrationPreview();
	}

	private void StopCalibrationCapture()
	{
		_calibrationActive = false;
		UpdateCalibrationPreview();
	}

	private void CaptureCalibrationCenter()
	{
		var binding = GetCalibrationBinding();
		if (binding == null || _lastReport == null)
		{
			SetCalibrationStatus("No live source for center.");
			return;
		}

		_calibrationCapture ??= AxisCalibrationCapture.From(binding.Axis);
		_calibrationCapture.CaptureCenter(ReportAnalyzer.ReadSourceValue(binding.Source, _lastReport));
		UpdateCalibrationPreview();
	}

	private void ApplyCalibration()
	{
		if (_draftProfile == null)
		{
			return;
		}

		var target = _calibrationTargetBox.SelectedItem is ControllerElement control ? control : ControllerElement.LeftStickX;
		var binding = GetCalibrationBinding();
		if (binding == null)
		{
			SetCalibrationStatus("No binding selected for calibration.");
			return;
		}

		_calibrationCapture ??= AxisCalibrationCapture.From(binding.Axis);
		var calibration = _calibrationCapture.ToCalibration(_invertCheck.IsChecked == true, _deadzoneSlider.Value, _saturationSlider.Value);
		_draftProfile = ProfileEditor.UpsertBinding(_draftProfile, binding with { Axis = calibration });
		_calibrationActive = false;
		UpdateBindingList();
		UpdateValidation();
		LoadCalibrationFromTarget();
		SetCalibrationStatus($"Applied calibration to {target}.");
	}

	private void LoadCalibrationFromTarget()
	{
		var binding = GetCalibrationBinding();
		var calibration = binding?.Axis;
		_invertCheck.IsChecked = calibration?.Invert ?? false;
		_deadzoneSlider.Value = calibration?.Deadzone ?? 0.1;
		_saturationSlider.Value = calibration?.Saturation ?? 1.0;
		_calibrationCapture = AxisCalibrationCapture.From(calibration);
		_calibrationActive = false;
		UpdateCalibrationPreview();
	}

	private void ObserveCalibration(byte[] report)
	{
		if (!_calibrationActive)
		{
			return;
		}

		var binding = GetCalibrationBinding();
		if (binding == null)
		{
			return;
		}

		_calibrationCapture ??= AxisCalibrationCapture.From(binding.Axis);
		_calibrationCapture.Observe(ReportAnalyzer.ReadSourceValue(binding.Source, report));
		UpdateCalibrationPreview();
	}

	private ControllerBinding? GetCalibrationBinding()
	{
		if (_draftProfile == null)
		{
			return null;
		}

		var target = _calibrationTargetBox.SelectedItem is ControllerElement control ? control : ControllerElement.LeftStickX;
		return _draftProfile.Bindings.FirstOrDefault(binding => binding.Target == target);
	}

	private void UpdateCalibrationPreview()
	{
		if (_calibrationCapture == null)
		{
			_calibrationPreviewText.Text = "Range: none";
			return;
		}

		var state = _calibrationActive ? "capturing" : "idle";
		_calibrationPreviewText.Text =
			$"Range: {_calibrationCapture.Minimum?.ToString() ?? "-"}..{_calibrationCapture.Maximum?.ToString() ?? "-"}   Center: {_calibrationCapture.Center?.ToString() ?? "-"}   Last: {_calibrationCapture.LastRaw?.ToString() ?? "-"}   Deadzone: {_deadzoneSlider.Value:0.00}   Saturation: {_saturationSlider.Value:0.00}   {state}";
	}

	private void UpdateBindingList()
	{
		_bindingsList.ItemsSource = _draftProfile?.Bindings
			.OrderBy(binding => binding.Target)
			.Select(binding => new BindingListItem(binding))
			.ToArray() ?? [];
	}

	private void UpdateValidation()
	{
		if (_draftProfile == null || _selectedDevice == null)
		{
			_validationText.Text = "";
			_saveProfileButton.IsEnabled = false;
			return;
		}

		var issues = ProfileEditor.ValidateProfile(_draftProfile, _selectedDevice.MaxInputReportLength);
		_validationText.Text = issues.Count == 0
			? $"Profile: {_draftProfile.Name}   Bindings: {_draftProfile.Bindings.Count}"
			: string.Join("\n", issues.Select(issue => issue.Message));
		_saveProfileButton.IsEnabled = issues.Count == 0;
	}

	private void UpdateSelectedDeviceDetails()
	{
		if (_selectedDevice == null)
		{
			return;
		}

		var mapping = GetMappingInfo(_selectedDevice);
		var mappingText = MappingDisplay.Format(mapping);
		var savedProfile = FindSavedProfile(_selectedDevice);
		var profileText = ProfileDocumentDisplay.Format(_profileStore.Path, savedProfile != null, _draftProfile);
		_controllerSummaryText.Text =
			$"{_selectedDevice.ProductName}\n{mappingText}\n{profileText}\nVID/PID: 0x{_selectedDevice.VendorId:X4}/0x{_selectedDevice.ProductId:X4}\nTransport: {_selectedDevice.Transport}\nInput report: {_selectedDevice.MaxInputReportLength} bytes\nReport IDs: {(_selectedDevice.ReportsUseId ? "yes" : "no")}\nUsage: {(_selectedDevice.IsGameControllerUsage ? "game controller" : "generic HID")}\n{_selectedDevice.Diagnostic}";
		_descriptorText.Text = mappingText + "\n" + profileText + "\n\nDescriptor\n" + ToHexRows(_selectedDevice.ReportDescriptor);
		_saveProfileButton.Content = "Save Profile";
		_testProfileButton.IsEnabled = savedProfile != null;
		_createProfileButton.IsEnabled = true;
		_editProfileButton.IsEnabled = savedProfile != null;
		_createProfileButton.Content = savedProfile == null ? "Create Profile" : "Create New Profile";
	}

	private void ShowSummaryWorkspace()
	{
		_controllerSummary.IsVisible = true;
		_tabs.IsVisible = false;
		_saveProfileButton.IsVisible = false;
		_newOverrideButton.IsVisible = false;
	}

	private void ShowTabbedWorkspace(WorkspaceMode mode, int tabIndex)
	{
		_controllerSummary.IsVisible = false;
		_tabs.IsVisible = true;
		_tabs.SelectedIndex = tabIndex;
		_saveProfileButton.IsVisible = mode == WorkspaceMode.EditProfile;
		_newOverrideButton.IsVisible = mode == WorkspaceMode.EditProfile;
		_newOverrideButton.IsEnabled = _selectedDevice != null;
	}

	private ControllerProfile? FindSavedProfile(HidDeviceInfo device)
		=> _profiles.FindMatch(new CopperControllerInfo(
			device.Id,
			device.ProductName,
			device.VendorId,
			device.ProductId,
			device.Transport,
			true,
			new HashSet<ControllerProfileKind> { ControllerProfileKind.RawInput },
			ControllerMappingSource.None,
			null,
			device.Diagnostic));

	private ControllerMappingInfo? GetMappingInfo(HidDeviceInfo device)
	{
		try
		{
			return _host.GetMappingInfo(device.Id);
		}
		catch (Exception ex)
		{
			SetStatus("Mapping lookup failed: " + ex.Message);
			return null;
		}
	}

	private void PostUi(string context, Action action)
		=> Dispatcher.UIThread.Post(() => TryRunUiAction(context, action));

	private void TryRunUiAction(string context, Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			CrashLog.Write(context, ex);
			SetStatus(context + ": " + ex.Message);
		}
	}

	private async Task TryRunUiActionAsync(string context, Func<Task> action)
	{
		try
		{
			await action().ConfigureAwait(true);
		}
		catch (Exception ex)
		{
			CrashLog.Write(context, ex);
			SetStatus(context + ": " + ex.Message);
		}
	}

	private void UpdateCaptureStatus(bool force = true)
	{
		var now = DateTimeOffset.UtcNow;
		if (!force)
		{
			if (now < _captureStatusHoldUntil)
			{
				return;
			}

			if (_lastCaptureStatusUpdate != DateTimeOffset.MinValue &&
				now - _lastCaptureStatusUpdate < CaptureStatusUpdateInterval)
			{
				return;
			}
		}

		if (_baselineReport == null)
		{
			SetCaptureStatus("Baseline: none");
			return;
		}

		if (_suggestedSource == null)
		{
			SetCaptureStatus($"Baseline: {_baselineReport.Length} bytes   Change: none");
			return;
		}

		SetCaptureStatus("Suggested source: " + ReportAnalyzer.FormatSource(_suggestedSource));
	}

	private void SetCaptureStatus(string text, TimeSpan? holdFor = null)
	{
		_captureStatusText.Text = text;
		_lastCaptureStatusUpdate = DateTimeOffset.UtcNow;
		_captureStatusHoldUntil = holdFor.HasValue
			? _lastCaptureStatusUpdate + holdFor.Value
			: DateTimeOffset.MinValue;
	}

	private void UpdateSourceFieldAvailability()
	{
		var kind = _sourceKindBox.SelectedItem is ControllerBindingSourceKind selectedKind
			? selectedKind
			: ControllerBindingSourceKind.ReportBit;
		_bitBox.IsEnabled = kind == ControllerBindingSourceKind.ReportBit;
		_hatBox.IsEnabled = kind == ControllerBindingSourceKind.Hat;
		_sourceInvertCheck.IsEnabled = kind == ControllerBindingSourceKind.ReportBit;
	}

	private void SetIndicator(ControllerElement control, bool active)
	{
		if (_indicators.TryGetValue(control, out var border))
		{
			border.Background = active ? Brushes.SeaGreen : Brushes.Transparent;
			border.BorderBrush = active ? Brushes.LightGreen : Brushes.DimGray;
		}
	}

	private Border CreateIndicator(ControllerElement control)
	{
		var border = new Border
		{
			BorderBrush = Brushes.DimGray,
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(4),
			Padding = new Thickness(10, 7),
			Margin = new Thickness(0, 0, 8, 8),
			Child = new TextBlock { Text = control.ToString(), VerticalAlignment = VerticalAlignment.Center }
		};
		_indicators[control] = border;
		return border;
	}

	private void SetStatus(string text)
		=> _statusText.Text = text;

	private void SetCalibrationStatus(string text)
	{
		_calibrationStatusText.Text = text;
		UpdateCalibrationPreview();
	}

	private static Button Button(string text, EventHandler<RoutedEventArgs> handler)
	{
		var button = new Button { Content = text };
		button.Click += handler;
		return button;
	}

	private static TextBlock TextBlock()
		=> new() { TextWrapping = TextWrapping.Wrap };

	private static TextBlock MonospaceTextBlock()
		=> new()
		{
			TextWrapping = TextWrapping.Wrap,
			FontFamily = new FontFamily("Consolas")
		};

	private static NumericUpDown NumberBox(int min, int max)
		=> new()
		{
			Minimum = min,
			Maximum = max,
			Increment = 1,
			Width = 130,
			MinWidth = 130
		};

	private static Control LabeledControl(string label, Control control)
	{
		var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 12, 8) };
		panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold });
		panel.Children.Add(control);
		return panel;
	}

	private static void AddFormRow(Grid grid, int row, string leftLabel, Control leftControl, string rightLabel, Control rightControl)
	{
		AddCell(grid, leftLabel, row, 0);
		AddCell(grid, leftControl, row, 1);
		AddCell(grid, rightLabel, row, 2);
		AddCell(grid, rightControl, row, 3);
	}

	private static void AddCell(Grid grid, string label, int row, int column)
		=> AddCell(grid, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, row, column);

	private static void AddCell(Grid grid, Control control, int row, int column)
	{
		Grid.SetRow(control, row);
		Grid.SetColumn(control, column);
		grid.Children.Add(control);
	}

	private static int DecimalToInt(decimal? value)
		=> (int)(value ?? 0);

	private sealed record PendingRawReport(HidDeviceInfo Device, byte[] Report, DateTimeOffset Timestamp);

	private static bool IsRecoverableHidException(Exception ex)
		=> ex is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException or NotSupportedException;

	private static string ToHexRows(byte[] bytes)
	{
		if (bytes.Length == 0)
		{
			return "";
		}

		var rows = new List<string>();
		for (var offset = 0; offset < bytes.Length; offset += 16)
		{
			var length = Math.Min(16, bytes.Length - offset);
			var hex = string.Join(" ", bytes.Skip(offset).Take(length).Select(value => value.ToString("X2")));
			rows.Add($"{offset:X4}: {hex}");
		}

		return string.Join(Environment.NewLine, rows);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_host.Dispose();
	}

	private sealed class DeviceListItem(HidDeviceInfo device)
	{
		public HidDeviceInfo Device { get; } = device;

		public override string ToString()
			=> $"{Device.ProductName}  0x{Device.VendorId:X4}:0x{Device.ProductId:X4}";
	}

	private sealed class BindingListItem(ControllerBinding binding)
	{
		public ControllerBinding Binding { get; } = binding;

		public override string ToString()
			=> $"{new MappingTargetItem(Binding.Target)}: {ReportAnalyzer.FormatSource(Binding.Source)}";
	}

	private enum WorkspaceMode
	{
		Test,
		EditProfile
	}
}

internal sealed class StickView : Control
{
	private double _x;
	private double _y;

	public StickView()
	{
		Width = 220;
		Height = 220;
		MinWidth = 180;
		MinHeight = 180;
	}

	public void SetPosition(double x, double y)
	{
		_x = Math.Clamp(x, -1, 1);
		_y = Math.Clamp(y, -1, 1);
		InvalidateVisual();
	}

	public override void Render(DrawingContext context)
	{
		base.Render(context);
		var size = Math.Min(Bounds.Width, Bounds.Height);
		var radius = Math.Max(10, (size / 2) - 12);
		var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
		context.DrawEllipse(Brushes.Transparent, new Pen(Brushes.Gray, 1), center, radius, radius);
		context.DrawLine(new Pen(Brushes.DimGray, 1), new Point(center.X - radius, center.Y), new Point(center.X + radius, center.Y));
		context.DrawLine(new Pen(Brushes.DimGray, 1), new Point(center.X, center.Y - radius), new Point(center.X, center.Y + radius));
		var dot = new Point(center.X + (_x * radius), center.Y - (_y * radius));
		context.DrawEllipse(Brushes.LightGreen, new Pen(Brushes.SeaGreen, 2), dot, 8, 8);
	}
}
