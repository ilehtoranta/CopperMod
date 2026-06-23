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
	private static readonly VirtualXboxControl[] AxisTargets =
	[
		VirtualXboxControl.LeftX,
		VirtualXboxControl.LeftY,
		VirtualXboxControl.RightX,
		VirtualXboxControl.RightY,
		VirtualXboxControl.LeftTrigger,
		VirtualXboxControl.RightTrigger
	];

	private readonly FileControllerProfileStore _profileStore = new(CopperPadProfilePaths.GetDefaultProfilePath());
	private readonly ControllerDiagnosticsHost _host;
	private readonly ListBox _deviceList = new();
	private readonly TextBlock _deviceDetails = TextBlock();
	private readonly TextBlock _statusText = TextBlock();
	private readonly TextBlock _stateText = TextBlock();
	private readonly TextBlock _rawHexText = MonospaceTextBlock();
	private readonly TextBlock _changedBytesText = TextBlock();
	private readonly TextBlock _descriptorText = MonospaceTextBlock();
	private readonly TextBlock _reportRateText = TextBlock();
	private readonly TextBlock _captureStatusText = TextBlock();
	private readonly TextBlock _validationText = TextBlock();
	private readonly TextBlock _calibrationStatusText = TextBlock();
	private readonly TextBlock _calibrationPreviewText = TextBlock();
	private readonly StickView _leftStick = new();
	private readonly StickView _rightStick = new();
	private readonly ProgressBar _leftTrigger = new() { Minimum = 0, Maximum = 1, Height = 16 };
	private readonly ProgressBar _rightTrigger = new() { Minimum = 0, Maximum = 1, Height = 16 };
	private readonly Dictionary<VirtualXboxControl, Border> _indicators = new();
	private readonly ComboBox _targetBox = new() { MinWidth = 170 };
	private readonly ComboBox _sourceKindBox = new() { MinWidth = 190 };
	private readonly NumericUpDown _offsetBox = NumberBox(0, 512);
	private readonly NumericUpDown _bitBox = NumberBox(0, 7);
	private readonly NumericUpDown _hatBox = NumberBox(0, 8);
	private readonly ListBox _bindingsList = new() { MinHeight = 180 };
	private readonly Button _useSuggestionButton = new() { Content = "Use Change", IsEnabled = false };
	private readonly Button _saveProfileButton = new() { Content = "Save Profile" };
	private readonly ComboBox _calibrationTargetBox = new() { MinWidth = 170 };
	private readonly CheckBox _invertCheck = new() { Content = "Invert" };
	private readonly Slider _deadzoneSlider = new() { Minimum = 0, Maximum = 0.95, Value = 0.1, Width = 180 };
	private readonly Slider _saturationSlider = new() { Minimum = 0.1, Maximum = 1, Value = 1, Width = 180 };
	private ControllerProfileSet _profiles = ControllerProfileSet.Empty;
	private ControllerProfile? _draftProfile;
	private HidDeviceInfo? _selectedDevice;
	private byte[]? _lastReport;
	private byte[]? _previousReport;
	private byte[]? _baselineReport;
	private ControllerBindingSource? _suggestedSource;
	private readonly Queue<DateTimeOffset> _reportTimes = new();
	private AxisCalibrationCapture? _calibrationCapture;
	private bool _calibrationActive;
	private bool _disposed;

	public MainWindow()
	{
		_host = new ControllerDiagnosticsHost(new ControllerHostOptions());
		Title = "CopperPad";
		Width = 1180;
		Height = 760;
		MinWidth = 980;
		MinHeight = 640;
		Content = BuildContent();

		_host.DevicesChanged += (_, args) => Dispatcher.UIThread.Post(() => OnDevicesChanged(args.Devices));
		_host.RawReportReceived += (_, args) => Dispatcher.UIThread.Post(() => OnRawReport(args));
		_host.StateChanged += (_, args) => Dispatcher.UIThread.Post(() => OnStateChanged(args.State));
		Opened += async (_, _) => await InitializeAsync().ConfigureAwait(true);
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
		toolbar.Children.Add(_saveProfileButton);
		toolbar.Children.Add(Button("Import", async (_, _) => await ImportProfilesAsync().ConfigureAwait(true)));
		toolbar.Children.Add(Button("Export", async (_, _) => await ExportProfilesAsync().ConfigureAwait(true)));
		_saveProfileButton.Click += async (_, _) => await SaveDraftProfileAsync().ConfigureAwait(true);
		root.Children.Add(toolbar);

		var layout = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("300,*"),
			RowDefinitions = new RowDefinitions("*"),
			Margin = new Thickness(12, 0, 12, 8)
		};
		layout.Children.Add(BuildDevicePane());
		var tabs = BuildTabs();
		Grid.SetColumn(tabs, 1);
		layout.Children.Add(tabs);
		root.Children.Add(layout);
		return root;
	}

	private Control BuildDevicePane()
	{
		var panel = new Grid
		{
			RowDefinitions = new RowDefinitions("Auto,*,Auto"),
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
		_deviceList.SelectionChanged += (_, _) =>
		{
			if (_deviceList.SelectedItem is DeviceListItem item)
			{
				SelectDevice(item.Device);
			}
		};
		Grid.SetRow(_deviceList, 1);
		panel.Children.Add(_deviceList);
		_deviceDetails.Margin = new Thickness(0, 10, 0, 0);
		Grid.SetRow(_deviceDetails, 2);
		panel.Children.Add(_deviceDetails);
		return panel;
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
			VirtualXboxControl.DPadUp,
			VirtualXboxControl.DPadDown,
			VirtualXboxControl.DPadLeft,
			VirtualXboxControl.DPadRight,
			VirtualXboxControl.A,
			VirtualXboxControl.B,
			VirtualXboxControl.X,
			VirtualXboxControl.Y,
			VirtualXboxControl.LeftShoulder,
			VirtualXboxControl.RightShoulder,
			VirtualXboxControl.Back,
			VirtualXboxControl.Start,
			VirtualXboxControl.Guide,
			VirtualXboxControl.LeftStick,
			VirtualXboxControl.RightStick
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
		_targetBox.ItemsSource = Enum.GetValues<VirtualXboxControl>();
		_targetBox.SelectedItem = VirtualXboxControl.A;
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
			}
		};

		var editor = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"),
			RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
			Margin = new Thickness(0, 0, 0, 12),
			ColumnSpacing = 8,
			RowSpacing = 8
		};
		AddFormRow(editor, 0, "Target", _targetBox, "Kind", _sourceKindBox);
		AddFormRow(editor, 1, "Offset", _offsetBox, "Bit", _bitBox);
		AddFormRow(editor, 2, "Hat", _hatBox, "", new StackPanel());
		var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
		actions.Children.Add(Button("Capture Baseline", (_, _) => CaptureBaseline()));
		actions.Children.Add(_useSuggestionButton);
		actions.Children.Add(Button("Add / Update", (_, _) => AddOrUpdateBinding()));
		actions.Children.Add(Button("Remove", (_, _) => RemoveSelectedBinding()));
		Grid.SetRow(actions, 3);
		Grid.SetColumnSpan(actions, 4);
		editor.Children.Add(actions);

		var root = new Grid
		{
			RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
			Margin = new Thickness(8)
		};
		root.Children.Add(editor);
		Grid.SetRow(_bindingsList, 1);
		root.Children.Add(_bindingsList);
		_captureStatusText.Margin = new Thickness(0, 10, 0, 0);
		Grid.SetRow(_captureStatusText, 2);
		root.Children.Add(_captureStatusText);
		_validationText.Margin = new Thickness(0, 8, 0, 0);
		Grid.SetRow(_validationText, 3);
		root.Children.Add(_validationText);
		UpdateSourceFieldAvailability();
		return root;
	}

	private Control BuildCalibrationTab()
	{
		_calibrationTargetBox.ItemsSource = AxisTargets;
		_calibrationTargetBox.SelectedItem = VirtualXboxControl.LeftX;
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
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
		{
			_profiles = ControllerProfileSet.Empty;
			SetStatus("Profile load failed: " + ex.Message);
		}

		_host.Start();
	}

	private void RefreshDevices()
	{
		_host.Stop();
		_host.Start();
	}

	private void OnDevicesChanged(IReadOnlyList<HidDeviceInfo> devices)
	{
		var selectedId = _selectedDevice?.Id;
		var items = devices.Select(device => new DeviceListItem(device)).ToArray();
		_deviceList.ItemsSource = items;
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

	private void SelectDevice(HidDeviceInfo? device)
	{
		_selectedDevice = device;
		_lastReport = null;
		_previousReport = null;
		_baselineReport = null;
		_suggestedSource = null;
		_reportTimes.Clear();
		_host.SelectDevice(device?.Id);
		if (device == null)
		{
			_deviceDetails.Text = "No controllers found.";
			_descriptorText.Text = "";
			_draftProfile = null;
			SetStatus("No controller selected.");
		}
		else
		{
			_deviceDetails.Text =
				$"{device.ProductName}\nVID/PID: 0x{device.VendorId:X4}/0x{device.ProductId:X4}\nTransport: {device.Transport}\nInput report: {device.MaxInputReportLength} bytes\nReport IDs: {(device.ReportsUseId ? "yes" : "no")}\nUsage: {(device.IsGameControllerUsage ? "game controller" : "generic HID")}\n{device.Diagnostic}";
			_descriptorText.Text = "Descriptor\n" + ToHexRows(device.ReportDescriptor);
			var info = new ControllerInfo(device.Id, device.ProductName, device.VendorId, device.ProductId, device.Transport, true, device.Diagnostic);
			_draftProfile = _profiles.FindMatch(info) ?? ProfileEditor.CreateDefaultProfile(device, DateTimeOffset.UtcNow);
			SetStatus("Selected " + device.ProductName);
		}

		UpdateBindingList();
		UpdateValidation();
		LoadCalibrationFromTarget();
		UpdateCaptureStatus();
	}

	private void OnRawReport(ControllerRawReportReceivedEventArgs args)
	{
		if (_selectedDevice == null || !string.Equals(args.Device.Id, _selectedDevice.Id, StringComparison.Ordinal))
		{
			return;
		}

		_previousReport = _lastReport;
		_lastReport = args.Report.ToArray();
		_rawHexText.Text = ToHexRows(_lastReport);
		_reportTimes.Enqueue(args.Timestamp);
		while (_reportTimes.Count > 0 && args.Timestamp - _reportTimes.Peek() > TimeSpan.FromSeconds(1))
		{
			_reportTimes.Dequeue();
		}

		_reportRateText.Text = $"Reports: {_reportTimes.Count}/s   Last: {args.Timestamp:HH:mm:ss.fff}";
		var changes = ReportAnalyzer.DetectChanges(_previousReport, _lastReport);
		_changedBytesText.Text = changes.Count == 0
			? "Changed bytes: none"
			: "Changed bytes: " + string.Join(", ", changes.Take(10).Select(change => $"{change.Offset}: {change.Baseline:X2}->{change.Current:X2}"));
		if (_baselineReport != null)
		{
			var best = ReportAnalyzer.GetBestChange(_baselineReport, _lastReport);
			_suggestedSource = best?.SuggestedSource;
			_useSuggestionButton.IsEnabled = _suggestedSource != null;
			UpdateCaptureStatus();
		}

		ObserveCalibration(_lastReport);
	}

	private void OnStateChanged(VirtualXboxControllerState state)
	{
		if (_selectedDevice == null || !string.Equals(state.ControllerId, _selectedDevice.Id, StringComparison.Ordinal))
		{
			return;
		}

		_leftStick.SetPosition(state.LeftX, state.LeftY);
		_rightStick.SetPosition(state.RightX, state.RightY);
		_leftTrigger.Value = state.LeftTrigger;
		_rightTrigger.Value = state.RightTrigger;
		SetIndicator(VirtualXboxControl.A, state.A);
		SetIndicator(VirtualXboxControl.B, state.B);
		SetIndicator(VirtualXboxControl.X, state.X);
		SetIndicator(VirtualXboxControl.Y, state.Y);
		SetIndicator(VirtualXboxControl.LeftShoulder, state.LeftShoulder);
		SetIndicator(VirtualXboxControl.RightShoulder, state.RightShoulder);
		SetIndicator(VirtualXboxControl.Back, state.Back);
		SetIndicator(VirtualXboxControl.Start, state.Start);
		SetIndicator(VirtualXboxControl.Guide, state.Guide);
		SetIndicator(VirtualXboxControl.LeftStick, state.LeftStick);
		SetIndicator(VirtualXboxControl.RightStick, state.RightStick);
		SetIndicator(VirtualXboxControl.DPadUp, state.DPadUp);
		SetIndicator(VirtualXboxControl.DPadDown, state.DPadDown);
		SetIndicator(VirtualXboxControl.DPadLeft, state.DPadLeft);
		SetIndicator(VirtualXboxControl.DPadRight, state.DPadRight);
		_stateText.Text =
			$"LX {state.LeftX:0.00}  LY {state.LeftY:0.00}  RX {state.RightX:0.00}  RY {state.RightY:0.00}  LT {state.LeftTrigger:0.00}  RT {state.RightTrigger:0.00}";
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
		UpdateCaptureStatus();
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
	}

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
		_targetBox.SelectedItem = binding.Target;
		SetSourceFields(binding.Source);
	}

	private void SetSourceFields(ControllerBindingSource source)
	{
		_sourceKindBox.SelectedItem = source.Kind;
		_offsetBox.Value = source.Offset;
		_bitBox.Value = source.Bit;
		_hatBox.Value = source.HatValue ?? 0;
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
			HatValue = kind == ControllerBindingSourceKind.Hat ? DecimalToInt(_hatBox.Value) : null
		};
	}

	private VirtualXboxControl GetSelectedTarget()
		=> _targetBox.SelectedItem is VirtualXboxControl control ? control : VirtualXboxControl.A;

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

		var target = _calibrationTargetBox.SelectedItem is VirtualXboxControl control ? control : VirtualXboxControl.LeftX;
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

		var target = _calibrationTargetBox.SelectedItem is VirtualXboxControl control ? control : VirtualXboxControl.LeftX;
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

	private void UpdateCaptureStatus()
	{
		if (_baselineReport == null)
		{
			_captureStatusText.Text = "Baseline: none";
			return;
		}

		if (_suggestedSource == null)
		{
			_captureStatusText.Text = $"Baseline: {_baselineReport.Length} bytes   Change: none";
			return;
		}

		_captureStatusText.Text = "Suggested source: " + ReportAnalyzer.FormatSource(_suggestedSource);
	}

	private void UpdateSourceFieldAvailability()
	{
		var kind = _sourceKindBox.SelectedItem is ControllerBindingSourceKind selectedKind
			? selectedKind
			: ControllerBindingSourceKind.ReportBit;
		_bitBox.IsEnabled = kind == ControllerBindingSourceKind.ReportBit;
		_hatBox.IsEnabled = kind == ControllerBindingSourceKind.Hat;
	}

	private void SetIndicator(VirtualXboxControl control, bool active)
	{
		if (_indicators.TryGetValue(control, out var border))
		{
			border.Background = active ? Brushes.SeaGreen : Brushes.Transparent;
			border.BorderBrush = active ? Brushes.LightGreen : Brushes.DimGray;
		}
	}

	private Border CreateIndicator(VirtualXboxControl control)
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
			Width = 90
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
			=> $"{Binding.Target}: {ReportAnalyzer.FormatSource(Binding.Source)}";
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
