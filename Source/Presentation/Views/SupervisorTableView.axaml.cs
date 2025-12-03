using AgentCommandEnvironment.Core.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;

namespace AgentCommandEnvironment.Presentation.Views
{
    public sealed partial class SupervisorTableView : UserControl
    {
        private const Double IndicatorColumnWidth = 32.0;
        private const Double ColumnPadding = 24.0;
        private const Double MeasurementSafetyPadding = 6.0;
        private const Double RowPadding = 8.0;
        private const Double DotDiameter = 10.0;
        private const Double DotSpacing = 4.0;
        private const Double DotHorizontalPadding = 6.0;

        private readonly Dictionary<SmartTask, SupervisorTaskSubscription> taskSubscriptions = new();
        private readonly Dictionary<SmartTask, IBrush> taskDotBrushes = new();
        private readonly Double[] columnPixelWidths;

        private ObservableCollection<SmartTask>? rootTasks;
        private Boolean rebuildPending;

        public event Action<SmartTask>? SmartTaskDetailsRequested;

        public SupervisorTableView()
        {
            InitializeComponent();
            UseLayoutRounding = true;
            columnPixelWidths = new Double[6];
            columnPixelWidths[0] = IndicatorColumnWidth;
        }

        public void AttachTasks(ObservableCollection<SmartTask>? tasks)
        {
            if (ReferenceEquals(rootTasks, tasks))
            {
                return;
            }

            if (rootTasks != null)
            {
                rootTasks.CollectionChanged -= OnRootTasksChanged;
                foreach (SmartTask task in rootTasks)
                {
                    DetachTaskRecursive(task);
                }
            }

            rootTasks = tasks;
            if (rootTasks != null)
            {
                rootTasks.CollectionChanged += OnRootTasksChanged;
                foreach (SmartTask task in rootTasks)
                {
                    AttachTaskRecursive(task);
                }
            }

            ScheduleRebuild();
        }

        private void OnRootTasksChanged(Object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (SmartTask task in e.OldItems)
                {
                    DetachTaskRecursive(task);
                }
            }

            if (e.NewItems != null)
            {
                foreach (SmartTask task in e.NewItems)
                {
                    AttachTaskRecursive(task);
                }
            }

            ScheduleRebuild();
        }

        private void AttachTaskRecursive(SmartTask task)
        {
            if (task == null)
            {
                return;
            }

            if (taskSubscriptions.ContainsKey(task))
            {
                return;
            }

            PropertyChangedEventHandler propertyHandler = (s, e) => ScheduleRebuild();
            NotifyCollectionChangedEventHandler subtasksHandler = (s, e) =>
            {
                if (e.OldItems != null)
                {
                    foreach (SmartTask oldTask in e.OldItems)
                    {
                        DetachTaskRecursive(oldTask);
                    }
                }
                if (e.NewItems != null)
                {
                    foreach (SmartTask newTask in e.NewItems)
                    {
                        AttachTaskRecursive(newTask);
                    }
                }
                ScheduleRebuild();
            };

            task.PropertyChanged += propertyHandler;
            task.Subtasks.CollectionChanged += subtasksHandler;
            taskSubscriptions[task] = new SupervisorTaskSubscription
            {
                PropertyHandler = propertyHandler,
                SubtasksHandler = subtasksHandler
            };

            foreach (SmartTask child in task.Subtasks)
            {
                AttachTaskRecursive(child);
            }
        }

        private void DetachTaskRecursive(SmartTask task)
        {
            if (task == null)
            {
                return;
            }

            if (taskSubscriptions.TryGetValue(task, out SupervisorTaskSubscription? subscription))
            {
                if (subscription.PropertyHandler != null)
                {
                    task.PropertyChanged -= subscription.PropertyHandler;
                }
                if (subscription.SubtasksHandler != null)
                {
                    task.Subtasks.CollectionChanged -= subscription.SubtasksHandler;
                }
                taskSubscriptions.Remove(task);
            }

            taskDotBrushes.Remove(task);

            foreach (SmartTask child in task.Subtasks)
            {
                DetachTaskRecursive(child);
            }
        }

        private void ScheduleRebuild()
        {
            if (rebuildPending)
            {
                return;
            }

            rebuildPending = true;
            Dispatcher.UIThread.Post(RebuildTable, DispatcherPriority.Background);
        }

        private void RebuildTable()
        {
            rebuildPending = false;

            List<SupervisorTableRowInfo> rows = new();
            if (rootTasks != null)
            {
                foreach (SmartTask task in rootTasks)
                {
                    AddRowRecursive(task, Array.Empty<SmartTask>(), rows);
                }
            }

            ComputeAndApplyColumnWidths(rows);

            RowsPanel.Children.Clear();
            if (rows.Count == 0)
            {
                TextBlock emptyText = new()
                {
                    Text = "Waiting for supervisor tasks...",
                    Margin = new Thickness(0, 8, 0, 8),
                    FontStyle = FontStyle.Italic,
                    Foreground = Brushes.Gray
                };
                RowsPanel.Children.Add(emptyText);
                return;
            }

            foreach (SupervisorTableRowInfo row in rows)
            {
                RowsPanel.Children.Add(BuildRow(row));
            }
        }

        private static void AddRowRecursive(SmartTask task, IReadOnlyList<SmartTask>? ancestors, List<SupervisorTableRowInfo> rows)
        {
            if (task == null)
            {
                return;
            }

            IReadOnlyList<SmartTask> safeAncestors = ancestors ?? Array.Empty<SmartTask>();
            rows.Add(new SupervisorTableRowInfo(task, safeAncestors));

            List<SmartTask> nextAncestors = new List<SmartTask>(safeAncestors.Count + 1);
            nextAncestors.AddRange(safeAncestors);
            nextAncestors.Add(task);

            foreach (SmartTask child in task.Subtasks)
            {
                AddRowRecursive(child, nextAncestors, rows);
            }
        }

        private void ComputeAndApplyColumnWidths(List<SupervisorTableRowInfo> rows)
        {
            Double[] widths = new Double[columnPixelWidths.Length];
            columnPixelWidths.CopyTo(widths, 0);
            Double indicatorWidth = IndicatorColumnWidth;
            if (rows.Count > 0)
            {
                Int32 maxDotCount = 1;
                foreach (SupervisorTableRowInfo row in rows)
                {
                    Int32 dotCount = row.Depth + 1;
                    if (dotCount > maxDotCount)
                    {
                        maxDotCount = dotCount;
                    }
                }

                Double requiredWidth = DotHorizontalPadding * 2.0 + (maxDotCount * DotDiameter) + Math.Max(0, maxDotCount - 1) * DotSpacing;
                indicatorWidth = Math.Max(indicatorWidth, requiredWidth);
            }
            widths[0] = indicatorWidth;

            widths[1] = Math.Max(widths[1], MeasureControlWidth(StateGlyphHeaderTextBlock) + ColumnPadding + MeasurementSafetyPadding);
            widths[2] = Math.Max(widths[2], MeasureControlWidth(IntentHeaderTextBlock) + ColumnPadding + MeasurementSafetyPadding);
            widths[3] = Math.Max(widths[3], MeasureControlWidth(StrategyHeaderTextBlock) + ColumnPadding + MeasurementSafetyPadding);
            widths[4] = Math.Max(widths[4], MeasureControlWidth(StatusHeaderTextBlock) + ColumnPadding + MeasurementSafetyPadding);
            widths[5] = Math.Max(widths[5], MeasureControlWidth(StageHeaderTextBlock) + ColumnPadding + MeasurementSafetyPadding);

            foreach (SupervisorTableRowInfo row in rows)
            {
                SmartTask task = row.Task;
                String intent = !String.IsNullOrWhiteSpace(task.Intent) ? task.Intent! : "(unspecified)";
                String retention = BuildRetentionSummary(task);
                String strategy = task.StrategyDisplay;
                String stateDisplay = task.StateDisplay;
                String stage = task.Stage ?? String.Empty;

                widths[1] = Math.Max(widths[1], MeasureTextWidth(task.StateIcon ?? String.Empty, StateGlyphHeaderTextBlock, FontWeight.Normal) + ColumnPadding + MeasurementSafetyPadding);
                Double intentWidth = Math.Max(
                    MeasureTextWidth(intent, IntentHeaderTextBlock, FontWeight.SemiBold),
                    MeasureTextWidth(retention, IntentHeaderTextBlock, FontWeight.Normal));
                widths[2] = Math.Max(widths[2], intentWidth + ColumnPadding + MeasurementSafetyPadding);
                widths[3] = Math.Max(widths[3], MeasureTextWidth(strategy, StrategyHeaderTextBlock, FontWeight.SemiBold) + ColumnPadding + MeasurementSafetyPadding);
                widths[4] = Math.Max(widths[4], MeasureTextWidth(stateDisplay, StatusHeaderTextBlock, FontWeight.Normal) + ColumnPadding + MeasurementSafetyPadding);
                widths[5] = Math.Max(widths[5], MeasureTextWidth(stage, StageHeaderTextBlock, FontWeight.SemiBold) + ColumnPadding + MeasurementSafetyPadding);
            }

            columnPixelWidths[0] = widths[0];
            for (Int32 i = 1; i < widths.Length; i++)
            {
                columnPixelWidths[i] = Math.Max(widths[i], 60);
            }

            if (HeaderGrid?.ColumnDefinitions != null && HeaderGrid.ColumnDefinitions.Count >= widths.Length)
            {
                for (Int32 index = 0; index < widths.Length; index++)
                {
                    HeaderGrid.ColumnDefinitions[index].Width = new GridLength(columnPixelWidths[index], GridUnitType.Pixel);
                }
            }
        }

        private Border BuildRow(SupervisorTableRowInfo row)
        {
            SmartTask task = row.Task;

            Border border = new()
            {
                BorderBrush = (this.TryFindResource("ThemeBorderBrush", out Object? borderBrush) ? borderBrush as IBrush : Brushes.LightGray),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent,
                Padding = new Thickness(6, RowPadding / 2, 6, RowPadding / 2)
            };

            Grid grid = new();
            for (Int32 index = 0; index < columnPixelWidths.Length; index++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(columnPixelWidths[index], GridUnitType.Pixel));
            }

            grid.Children.Add(BuildIndicatorCell(row));

            TextBlock stateGlyph = new()
            {
                Text = task.StateIcon,
                FontFamily = FontFamily.Parse("Segoe UI Symbol"),
                FontSize = 18,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(stateGlyph, 1);
            grid.Children.Add(stateGlyph);

            StackPanel intentStack = new()
            {
                Spacing = 2,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            intentStack.Children.Add(new TextBlock
            {
                Text = !String.IsNullOrWhiteSpace(task.Intent) ? task.Intent! : "(unspecified)",
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            intentStack.Children.Add(new TextBlock
            {
                Text = BuildRetentionSummary(task),
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            Grid.SetColumn(intentStack, 2);
            grid.Children.Add(intentStack);

            TextBlock strategyText = new()
            {
                Text = task.StrategyDisplay,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(strategyText, 3);
            grid.Children.Add(strategyText);

            TextBlock stateDisplayText = new()
            {
                Text = task.StateDisplay,
                FontSize = 11,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(stateDisplayText, 4);
            grid.Children.Add(stateDisplayText);

            TextBlock stageText = new()
            {
                Text = task.Stage ?? String.Empty,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(stageText, 5);
            grid.Children.Add(stageText);

            border.Child = grid;

            border.PointerPressed += (s, e) =>
            {
                if (e.ClickCount >= 2)
                {
                    SmartTaskDetailsRequested?.Invoke(task);
                    e.Handled = true;
                }
            };

            ContextMenu contextMenu = new();
            MenuItem detailsItem = new()
            {
                Header = "Details"
            };
            detailsItem.Click += (s, e) => SmartTaskDetailsRequested?.Invoke(task);
            contextMenu.ItemsSource = new[] { detailsItem };
            border.ContextMenu = contextMenu;

            return border;
        }

        private Control BuildIndicatorCell(SupervisorTableRowInfo row)
        {
            SmartTask task = row.Task;
            Grid container = new()
            {
                Width = columnPixelWidths[0],
                Height = 26,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
            };

            StackPanel dotPanel = new()
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = DotSpacing,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(DotHorizontalPadding, 0, 0, 0)
            };

            if (row.Ancestors.Count > 0)
            {
                foreach (SmartTask ancestor in row.Ancestors)
                {
                    dotPanel.Children.Add(CreateDotBorder(ancestor));
                }
            }

            dotPanel.Children.Add(CreateDotBorder(task));

            container.Children.Add(dotPanel);
            Grid.SetColumn(container, 0);
            return container;
        }

        private Border CreateDotBorder(SmartTask task)
        {
            IBrush dotBrush = GetOrCreateDotBrush(task);
            return new Border
            {
                Width = DotDiameter,
                Height = DotDiameter,
                CornerRadius = new CornerRadius(DotDiameter / 2.0),
                Background = dotBrush,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
        }

        private IBrush GetOrCreateDotBrush(SmartTask task)
        {
            if (task == null)
            {
                return Brushes.Gray;
            }

            if (taskDotBrushes.TryGetValue(task, out IBrush? brush))
            {
                return brush;
            }

            Color taskColor = CreateColorForTask(task);
            SolidColorBrush solidBrush = new(taskColor);
            taskDotBrushes[task] = solidBrush;
            return solidBrush;
        }

        private static Color CreateColorForTask(SmartTask task)
        {
            String seed = !String.IsNullOrWhiteSpace(task.Id)
                ? task.Id
                : (!String.IsNullOrWhiteSpace(task.Intent) ? task.Intent! : task.GetHashCode().ToString(CultureInfo.InvariantCulture));

            UInt32 hash = 2166136261;
            foreach (Char c in seed)
            {
                unchecked
                {
                    hash ^= c;
                    hash *= 16777619;
                }
            }

            Double hue = hash % 360u;
            const Double saturation = 0.65;
            const Double value = 0.85;
            return ConvertHsvToColor(hue, saturation, value);
        }

        private static Color ConvertHsvToColor(Double hue, Double saturation, Double value)
        {
            hue = hue % 360.0;
            if (hue < 0)
            {
                hue += 360.0;
            }

            Double chroma = value * saturation;
            Double sector = hue / 60.0;
            Double x = chroma * (1 - Math.Abs(sector % 2 - 1));
            Double m = value - chroma;

            Double rPrime;
            Double gPrime;
            Double bPrime;
            if (sector < 1)
            {
                rPrime = chroma;
                gPrime = x;
                bPrime = 0.0;
            }
            else if (sector < 2)
            {
                rPrime = x;
                gPrime = chroma;
                bPrime = 0.0;
            }
            else if (sector < 3)
            {
                rPrime = 0.0;
                gPrime = chroma;
                bPrime = x;
            }
            else if (sector < 4)
            {
                rPrime = 0.0;
                gPrime = x;
                bPrime = chroma;
            }
            else if (sector < 5)
            {
                rPrime = x;
                gPrime = 0.0;
                bPrime = chroma;
            }
            else
            {
                rPrime = chroma;
                gPrime = 0.0;
                bPrime = x;
            }

            Byte r = (Byte)Math.Round((rPrime + m) * 255.0, MidpointRounding.AwayFromZero);
            Byte g = (Byte)Math.Round((gPrime + m) * 255.0, MidpointRounding.AwayFromZero);
            Byte b = (Byte)Math.Round((bPrime + m) * 255.0, MidpointRounding.AwayFromZero);
            return Color.FromRgb(r, g, b);
        }

        private static String BuildRetentionSummary(SmartTask task)
        {
            String retained = task.WorkRetentionPercentDisplay;
            String delegated = task.DelegationPercentDisplay;
            return retained + " retained \u00B7 " + delegated + " delegated";
        }

        private static Double MeasureControlWidth(Control? control)
        {
            if (control == null)
            {
                return 0.0;
            }
            control.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            return control.DesiredSize.Width;
        }

        private static Double MeasureTextWidth(String text, TextBlock? reference, FontWeight forcedWeight)
        {
            TextBlock textBlock = new()
            {
                Text = text ?? String.Empty,
                FontWeight = forcedWeight,
                TextWrapping = TextWrapping.NoWrap
            };
            if (reference != null)
            {
                textBlock.FontFamily = reference.FontFamily;
                textBlock.FontSize = reference.FontSize;
                textBlock.FontStyle = reference.FontStyle;
                textBlock.FontStretch = reference.FontStretch;
            }
            textBlock.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            return textBlock.DesiredSize.Width;
        }
    }
}




