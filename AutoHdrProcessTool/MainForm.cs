using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using AutoHdrProcessTool.Localization;
using AutoHdrProcessTool.Models;
using AutoHdrProcessTool.Services;
using AutoHdrProcessTool.UI;

namespace AutoHdrProcessTool
{
    internal sealed class MainForm : Form
    {
        private readonly ProcessCatalog processCatalog = new ProcessCatalog();
        private readonly AutoHdrProfileService profileService = new AutoHdrProfileService();
        private readonly AutoHdrIntensityService intensityService = new AutoHdrIntensityService();

        private readonly DataGridView processGrid;
        private readonly TextBox filterTextBox;
        private readonly Button refreshButton;
        private readonly Label processNameValue;
        private readonly Label processIdValue;
        private readonly Label processPathValue;
        private readonly Label ruleStateValue;
        private readonly CheckBox enable10BitCheckBox;
        private readonly Button enableButton;
        private readonly Button disableButton;
        private readonly TrackBar strengthTrackBar;
        private readonly NumericUpDown strengthNumber;
        private readonly Button applyStrengthButton;
        private readonly Label intensityCapabilityLabel;
        private readonly ToolStripStatusLabel statusLabel;

        private IList<ProcessEntry> allProcesses = new List<ProcessEntry>();
        private ProcessEntry selectedProcess;
        private bool intensityAvailable;
        private bool synchronizingStrength;

        public MainForm()
        {
            Text = Strings.Get("WindowTitle");
            using (Icon executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
            {
                if (executableIcon != null)
                {
                    Icon = (Icon)executableIcon.Clone();
                }
            }

            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            MinimumSize = new Size(980, 600);
            ClientSize = new Size(1160, 568);
            KeyPreview = true;

            var content = BuildContent(
                out filterTextBox,
                out refreshButton,
                out processGrid,
                out processNameValue,
                out processIdValue,
                out processPathValue,
                out ruleStateValue,
                out enable10BitCheckBox,
                out enableButton,
                out disableButton,
                out strengthTrackBar,
                out strengthNumber,
                out applyStrengthButton,
                out intensityCapabilityLabel);
            var statusStrip = new StatusStrip
            {
                SizingGrip = false,
                Padding = new Padding(10, 0, 10, 0)
            };
            statusLabel = new ToolStripStatusLabel(Strings.Get("StatusInitializing"))
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            statusStrip.Items.Add(statusLabel);

            Controls.Add(content);
            Controls.Add(statusStrip);

            filterTextBox.TextChanged += (sender, args) => ApplyFilter();
            refreshButton.Click += (sender, args) => RefreshProcesses();
            processGrid.SelectionChanged += (sender, args) => LoadSelectedProcess();
            enableButton.Click += (sender, args) => EnableAutoHdr();
            disableButton.Click += (sender, args) => DisableAutoHdr();
            applyStrengthButton.Click += (sender, args) => ApplyStrength();
            strengthTrackBar.ValueChanged += StrengthTrackBarOnValueChanged;
            strengthNumber.ValueChanged += StrengthNumberOnValueChanged;
            Shown += MainFormOnShown;
            KeyDown += MainFormOnKeyDown;

            ThemeManager.Apply(this, ThemeManager.IsSystemDark());
            SetSelectionControlsEnabled(false);
        }

        private Panel BuildContent(
            out TextBox filter,
            out Button refresh,
            out DataGridView grid,
            out Label nameValue,
            out Label idValue,
            out Label pathValue,
            out Label stateValue,
            out CheckBox tenBit,
            out Button enable,
            out Button disable,
            out TrackBar strengthSlider,
            out NumericUpDown strengthInput,
            out Button applyStrength,
            out Label capabilityLabel)
        {
            var content = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16, 14, 16, 14)
            };
            var split = new SplitContainer
            {
                Size = new Size(1128, 514),
                Dock = DockStyle.Top,
                SplitterWidth = 8,
                Panel1MinSize = 480,
                Panel2MinSize = 360,
                SplitterDistance = 650,
                FixedPanel = FixedPanel.None
            };
            split.Panel1.Padding = new Padding(0, 0, 4, 0);
            split.Panel2.Padding = new Padding(4, 0, 0, 0);
            split.SizeChanged += (sender, args) =>
            {
                int available = split.ClientSize.Width - split.SplitterWidth;
                int minimum = split.Panel1MinSize;
                int maximum = available - split.Panel2MinSize;
                if (maximum < minimum)
                {
                    return;
                }

                int desired = (int)Math.Round(available * 0.58, MidpointRounding.AwayFromZero);
                desired = Math.Max(minimum, Math.Min(maximum, desired));
                if (split.SplitterDistance != desired)
                {
                    split.SplitterDistance = desired;
                }
            };

            split.Panel1.Controls.Add(BuildProcessPanel(out filter, out refresh, out grid));
            split.Panel2.Controls.Add(BuildSettingsPanel(
                out nameValue,
                out idValue,
                out pathValue,
                out stateValue,
                out tenBit,
                out enable,
                out disable,
                out strengthSlider,
                out strengthInput,
                out applyStrength,
                out capabilityLabel));
            content.Controls.Add(split);
            return content;
        }

        private Control BuildProcessPanel(
            out TextBox filter,
            out Button refresh,
            out DataGridView grid)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var searchLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty
            };
            searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42F));
            searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            searchLayout.Controls.Add(new Label
            {
                Text = Strings.Get("FilterLabel"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty
            }, 0, 0);
            filter = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 7, 8, 7)
            };
            refresh = new Button
            {
                Text = Strings.Get("RefreshButton"),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 5),
                AccessibleName = Strings.Get("RefreshProcessListAccessibleName")
            };
            searchLayout.Controls.Add(filter, 1, 0);
            searchLayout.Controls.Add(refresh, 2, 0);

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowTemplate = { Height = 28 },
                ColumnHeadersHeight = 34,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(ProcessEntry.ProcessName),
                HeaderText = Strings.Get("ProcessColumn"),
                Width = 150,
                MinimumWidth = 90
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(ProcessEntry.ProcessId),
                HeaderText = Strings.Get("PidColumn"),
                Width = 70,
                MinimumWidth = 60
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(ProcessEntry.AutoHdrStatus),
                HeaderText = Strings.Get("AutoHdrColumn"),
                Width = 84,
                MinimumWidth = 78
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(ProcessEntry.DisplayPath),
                HeaderText = Strings.Get("ImagePathColumn"),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 180
            });

            layout.Controls.Add(searchLayout, 0, 0);
            layout.Controls.Add(grid, 0, 1);
            return layout;
        }

        private Control BuildSettingsPanel(
            out Label nameValue,
            out Label idValue,
            out Label pathValue,
            out Label stateValue,
            out CheckBox tenBit,
            out Button enable,
            out Button disable,
            out TrackBar strengthSlider,
            out NumericUpDown strengthInput,
            out Button applyStrength,
            out Label capabilityLabel)
        {
            var scrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 3,
                Padding = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.Controls.Add(BuildSelectionGroup(out nameValue, out idValue, out pathValue), 0, 0);
            layout.Controls.Add(BuildProfileGroup(out stateValue, out tenBit, out enable, out disable), 0, 1);
            layout.Controls.Add(BuildIntensityGroup(
                out strengthSlider,
                out strengthInput,
                out applyStrength,
                out capabilityLabel), 0, 2);
            scrollPanel.Controls.Add(layout);
            return scrollPanel;
        }

        private GroupBox BuildSelectionGroup(out Label nameValue, out Label idValue, out Label pathValue)
        {
            var group = new GroupBox
            {
                Text = Strings.Get("SelectedProcessGroup"),
                Dock = DockStyle.Top,
                Height = 134,
                Padding = new Padding(12, 8, 12, 10),
                Margin = new Padding(0, 0, 0, 10)
            };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.Controls.Add(CreateFieldLabel(Strings.Get("NameField")), 0, 0);
            nameValue = CreateValueLabel();
            layout.Controls.Add(nameValue, 1, 0);
            layout.Controls.Add(CreateFieldLabel(Strings.Get("PidColumn")), 0, 1);
            idValue = CreateValueLabel();
            layout.Controls.Add(idValue, 1, 1);
            layout.Controls.Add(CreateFieldLabel(Strings.Get("PathField")), 0, 2);
            pathValue = CreateValueLabel();
            pathValue.AutoEllipsis = true;
            layout.Controls.Add(pathValue, 1, 2);
            group.Controls.Add(layout);
            return group;
        }

        private GroupBox BuildProfileGroup(
            out Label stateValue,
            out CheckBox tenBit,
            out Button enable,
            out Button disable)
        {
            var group = new GroupBox
            {
                Text = Strings.Get("AutoHdrRuleGroup"),
                Dock = DockStyle.Top,
                Height = 184,
                Padding = new Padding(12, 8, 12, 10),
                Margin = new Padding(0, 0, 0, 10)
            };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            stateValue = new Label
            {
                Text = Strings.Get("NoProcessSelected"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Margin = Padding.Empty
            };
            tenBit = new CheckBox
            {
                Text = Strings.Get("Enable10BitBufferUpgrade"),
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            enable = new Button
            {
                Text = Strings.Get("EnableAutoHdrButton"),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 5, 5),
                Tag = "accent"
            };
            disable = new Button
            {
                Text = Strings.Get("RemoveRuleButton"),
                Dock = DockStyle.Fill,
                Margin = new Padding(5, 6, 0, 5)
            };
            buttons.Controls.Add(enable, 0, 0);
            buttons.Controls.Add(disable, 1, 0);
            layout.Controls.Add(stateValue, 0, 0);
            layout.Controls.Add(tenBit, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            layout.Controls.Add(new Label
            {
                Text = Strings.Get("RuleRestartHint"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = Padding.Empty,
                Tag = "muted"
            }, 0, 3);
            group.Controls.Add(layout);
            return group;
        }

        private GroupBox BuildIntensityGroup(
            out TrackBar strengthSlider,
            out NumericUpDown strengthInput,
            out Button applyStrength,
            out Label capabilityLabel)
        {
            var group = new GroupBox
            {
                Text = Strings.Get("LiveIntensityGroup"),
                Dock = DockStyle.Top,
                Height = 176,
                Padding = new Padding(12, 8, 12, 10),
                Margin = Padding.Empty
            };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            capabilityLabel = new Label
            {
                Text = Strings.Get("CheckingSystemInterface"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = Padding.Empty,
                Tag = "muted"
            };

            var valueLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };
            valueLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            valueLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74F));
            strengthSlider = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                SmallChange = 1,
                LargeChange = 10,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 8, 0)
            };
            strengthInput = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 100,
                Increment = 1,
                Dock = DockStyle.Fill,
                TextAlign = HorizontalAlignment.Right,
                Margin = new Padding(0, 12, 0, 14)
            };
            valueLayout.Controls.Add(strengthSlider, 0, 0);
            valueLayout.Controls.Add(strengthInput, 1, 0);

            applyStrength = new Button
            {
                Text = Strings.Get("ApplyCurrentProcessButton"),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 0, 5),
                Tag = "accent"
            };
            layout.Controls.Add(capabilityLabel, 0, 0);
            layout.Controls.Add(valueLayout, 0, 1);
            layout.Controls.Add(applyStrength, 0, 2);
            group.Controls.Add(layout);
            return group;
        }

        private static Label CreateFieldLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
                Tag = "muted"
            };
        }

        private static Label CreateValueLabel()
        {
            return new Label
            {
                Text = Strings.Get("NotAvailableValue"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty
            };
        }

        private void MainFormOnShown(object sender, EventArgs eventArgs)
        {
            string capabilityMessage;
            intensityAvailable = intensityService.TryGetCapability(out capabilityMessage);
            intensityCapabilityLabel.Text = capabilityMessage;
            RefreshProcesses();
        }

        private void MainFormOnKeyDown(object sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.KeyCode == Keys.F5)
            {
                RefreshProcesses();
                eventArgs.Handled = true;
            }
        }

        private void RefreshProcesses()
        {
            int? selectedId = selectedProcess == null ? (int?)null : selectedProcess.ProcessId;
            refreshButton.Enabled = false;
            Cursor = Cursors.WaitCursor;
            SetStatus(Strings.Get("ReadingProcessList"));
            try
            {
                allProcesses = processCatalog.GetProcesses();
                ApplyFilter(selectedId);
                SetStatus(Strings.Format("ProcessesRead", allProcesses.Count), false);
            }
            catch (Exception exception)
            {
                ShowError(Strings.Get("RefreshProcessListFailed"), exception);
            }
            finally
            {
                Cursor = Cursors.Default;
                refreshButton.Enabled = true;
            }
        }

        private void ApplyFilter(int? preferredProcessId = null)
        {
            string filter = filterTextBox.Text.Trim();
            List<ProcessEntry> visible = allProcesses.Where(entry =>
                filter.Length == 0 ||
                entry.ProcessName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                entry.ProcessId.ToString(CultureInfo.InvariantCulture).Contains(filter) ||
                (!string.IsNullOrEmpty(entry.ImagePath) &&
                 entry.ImagePath.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToList();

            processGrid.DataSource = visible;
            processGrid.ClearSelection();
            if (visible.Count == 0)
            {
                selectedProcess = null;
                ShowSelectedProcess(null);
                return;
            }

            int rowIndex = visible.FindIndex(entry => entry.PathAvailable);
            if (rowIndex < 0)
            {
                rowIndex = 0;
            }
            if (preferredProcessId.HasValue)
            {
                int matchingIndex = visible.FindIndex(entry => entry.ProcessId == preferredProcessId.Value);
                if (matchingIndex >= 0)
                {
                    rowIndex = matchingIndex;
                }
            }

            processGrid.Rows[rowIndex].Selected = true;
            processGrid.CurrentCell = processGrid.Rows[rowIndex].Cells[0];
        }

        private void LoadSelectedProcess()
        {
            if (processGrid.SelectedRows.Count == 0)
            {
                return;
            }

            var entry = processGrid.SelectedRows[0].DataBoundItem as ProcessEntry;
            if (entry == null || ReferenceEquals(entry, selectedProcess))
            {
                return;
            }

            selectedProcess = entry;
            ShowSelectedProcess(entry);
        }

        private void ShowSelectedProcess(ProcessEntry entry)
        {
            if (entry == null)
            {
                selectedProcess = null;
                processNameValue.Text = Strings.Get("NotAvailableValue");
                processIdValue.Text = Strings.Get("NotAvailableValue");
                processPathValue.Text = Strings.Get("NotAvailableValue");
                processPathValue.Tag = null;
                ruleStateValue.Text = Strings.Get("NoProcessSelected");
                SetSelectionControlsEnabled(false);
                return;
            }

            processNameValue.Text = entry.ProcessName;
            processIdValue.Text = entry.ProcessId.ToString(CultureInfo.CurrentCulture);
            processPathValue.Text = entry.DisplayPath;
            processPathValue.Tag = entry.PathAvailable ? entry.ImagePath : null;
            SetSelectionControlsEnabled(entry.PathAvailable);
            if (!entry.PathAvailable)
            {
                entry.AutoHdrStatus = Strings.Get("StatusUnavailable");
                ruleStateValue.Text = Strings.Get("FullPathUnavailable");
                processGrid.Refresh();
                return;
            }

            try
            {
                AutoHdrRuleState state = profileService.GetState(entry.ImagePath);
                entry.AutoHdrStatus = state.Enabled
                    ? Strings.Get("StatusEnabled")
                    : Strings.Get("StatusDisabled");
                ruleStateValue.Text = state.Enabled
                    ? Strings.Get("RuleEnabledForExe")
                    : Strings.Get("RuleNotEnabledForExe");
                enable10BitCheckBox.Checked = state.Enable10Bit;
                processGrid.Refresh();
            }
            catch (Exception exception)
            {
                entry.AutoHdrStatus = Strings.Get("StatusCheckFailed");
                ruleStateValue.Text = Strings.Get("RuleReadFailed");
                processGrid.Refresh();
                SetStatus(Strings.Format("RuleReadError", exception.Message), true);
            }

            if (intensityAvailable)
            {
                try
                {
                    uint strength = intensityService.GetLiveStrength(entry.ImagePath);
                    SetStrengthValue((int)strength);
                }
                catch (Exception exception)
                {
                    SetStatus(Strings.Format("LiveIntensityReadError", exception.Message), true);
                }
            }
        }

        private void SetSelectionControlsEnabled(bool pathAvailable)
        {
            enable10BitCheckBox.Enabled = pathAvailable;
            enableButton.Enabled = pathAvailable;
            disableButton.Enabled = pathAvailable;
            strengthTrackBar.Enabled = pathAvailable && intensityAvailable;
            strengthNumber.Enabled = pathAvailable && intensityAvailable;
            applyStrengthButton.Enabled = pathAvailable && intensityAvailable;
        }

        private void EnableAutoHdr()
        {
            ProcessEntry entry = selectedProcess;
            if (entry == null || !entry.PathAvailable)
            {
                return;
            }

            RunAction(Strings.Get("EnableAutoHdrFailed"), () =>
            {
                processCatalog.EnsureProcessMatches(entry.ProcessId, entry.ImagePath);
                profileService.Enable(entry.ImagePath, enable10BitCheckBox.Checked);
                AutoHdrRuleState state = profileService.GetState(entry.ImagePath);
                if (!state.Enabled)
                {
                    throw new InvalidOperationException(Strings.Get("RuleVerificationFailed"));
                }

                entry.AutoHdrStatus = Strings.Get("StatusEnabled");
                ruleStateValue.Text = Strings.Get("RuleEnabledForExe");
                processGrid.Refresh();
                SetStatus(Strings.Get("RuleSavedRestartRequired"), false);
            });
        }

        private void DisableAutoHdr()
        {
            ProcessEntry entry = selectedProcess;
            if (entry == null || !entry.PathAvailable)
            {
                return;
            }

            RunAction(Strings.Get("RemoveRuleFailed"), () =>
            {
                processCatalog.EnsureProcessMatches(entry.ProcessId, entry.ImagePath);
                profileService.Disable(entry.ImagePath);
                AutoHdrRuleState state = profileService.GetState(entry.ImagePath);
                if (state.Enabled)
                {
                    throw new InvalidOperationException(Strings.Get("RuleStillEnabled"));
                }

                entry.AutoHdrStatus = Strings.Get("StatusDisabled");
                ruleStateValue.Text = Strings.Get("RuleNotEnabledForExe");
                processGrid.Refresh();
                SetStatus(Strings.Get("RuleRemovedRestartRequired"), false);
            });
        }

        private void ApplyStrength()
        {
            ProcessEntry entry = selectedProcess;
            if (entry == null || !entry.PathAvailable || !intensityAvailable)
            {
                return;
            }

            uint strength = decimal.ToUInt32(strengthNumber.Value);
            RunAction(Strings.Get("ApplyStrengthFailed"), () =>
            {
                processCatalog.EnsureProcessMatches(entry.ProcessId, entry.ImagePath);
                intensityService.SetLiveStrength(entry.ImagePath, entry.ProcessId, strength);
                uint actual = intensityService.GetLiveStrength(entry.ImagePath);
                SetStrengthValue((int)actual);
                SetStatus(Strings.Format("StrengthSetStatus", actual), false);
            });
        }

        private void StrengthTrackBarOnValueChanged(object sender, EventArgs eventArgs)
        {
            if (synchronizingStrength)
            {
                return;
            }

            synchronizingStrength = true;
            strengthNumber.Value = strengthTrackBar.Value;
            synchronizingStrength = false;
        }

        private void StrengthNumberOnValueChanged(object sender, EventArgs eventArgs)
        {
            if (synchronizingStrength)
            {
                return;
            }

            synchronizingStrength = true;
            strengthTrackBar.Value = decimal.ToInt32(strengthNumber.Value);
            synchronizingStrength = false;
        }

        private void SetStrengthValue(int value)
        {
            value = Math.Max(strengthTrackBar.Minimum, Math.Min(strengthTrackBar.Maximum, value));
            synchronizingStrength = true;
            strengthTrackBar.Value = value;
            strengthNumber.Value = value;
            synchronizingStrength = false;
        }

        private void RunAction(string title, Action action)
        {
            UseWaitCursor = true;
            try
            {
                action();
            }
            catch (Exception exception)
            {
                ShowError(title, exception);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void ShowError(string title, Exception exception)
        {
            SetStatus(title + ": " + exception.Message, true);
            MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void SetStatus(string text, bool isError = false)
        {
            statusLabel.Text = text;
            ThemePalette palette = ThemeManager.Current;
            statusLabel.ForeColor = isError
                ? Color.FromArgb(214, 74, 74)
                : palette == null ? SystemColors.ControlText : palette.MutedText;
        }

        protected override void WndProc(ref Message message)
        {
            const int WindowMessageSettingChange = 0x001A;
            base.WndProc(ref message);
            if (message.Msg == WindowMessageSettingChange && IsHandleCreated && !IsDisposed)
            {
                BeginInvoke((Action)(() => ThemeManager.Apply(this, ThemeManager.IsSystemDark())));
            }
        }
    }
}
