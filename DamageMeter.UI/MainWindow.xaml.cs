﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DamageMeter.AutoUpdate;
using DamageMeter.Sniffing;
using DamageMeter.UI.EntityStats;
using Data;
using log4net;
using Application = System.Windows.Forms.Application;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace DamageMeter.UI
{
    /// <summary>
    ///     Logique d'interaction pour MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private readonly EntityStatsMain _entityStats;
        private bool _keyboardInitialized;

        public MainWindow()
        {
            InitializeComponent();
            var currentDomain = default(AppDomain);
            currentDomain = AppDomain.CurrentDomain;
            // Handler for unhandled exceptions.
            currentDomain.UnhandledException += GlobalUnhandledExceptionHandler;
            // Handler for exceptions in threads behind forms.
            Application.ThreadException += GlobalThreadExceptionHandler;
            var init = BasicTeraData.Instance;

            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Idle;
            TeraSniffer.Instance.Enabled = true;
            NetworkController.Instance.Connected += HandleConnected;
            NetworkController.Instance.TickUpdated += Update;
            DamageTracker.Instance.CurrentBossUpdated += SelectEncounter;
            var dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Tick += UpdateKeyboard;
            dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
            dispatcherTimer.Start();
            PinImage.Source = BasicTeraData.Instance.PinData.UnPin.Source;
            EntityStatsImage.Source = BasicTeraData.Instance.PinData.EntityStats.Source;
            ListEncounter.PreviewKeyDown += ListEncounterOnPreviewKeyDown;
            UpdateComboboxEncounter(new LinkedList<Entity>());
            Title = "Shinra Meter V" + UpdateManager.Version;
            BackgroundColor.Opacity = BasicTeraData.Instance.WindowData.MainWindowOpacity;
            _entityStats = new EntityStatsMain(this);
        }

      

        public Dictionary<PlayerInfo, PlayerStats> Controls { get; set; } = new Dictionary<PlayerInfo, PlayerStats>();

        private void ListEncounterOnPreviewKeyDown(object sender, KeyEventArgs keyEventArgs)
        {
            keyEventArgs.Handled = true;
        }

        private static void GlobalUnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            MessageBox.Show("An fatal error has be found, please see the error.log file for more informations.");
            var ex = default(Exception);
            ex = (Exception) e.ExceptionObject;
            var log = LogManager.GetLogger(typeof (Program));
            log.Error("##### CRASH (version=" + UpdateManager.Version + "): #####\r\n" + ex.Message + "\r\n" +
                      ex.StackTrace + "\r\n" + ex.Source + "\r\n" + ex + "\r\n" + ex.Data + "\r\n" + ex.InnerException +
                      "\r\n" + ex.TargetSite);
        }

        private static void GlobalThreadExceptionHandler(object sender, ThreadExceptionEventArgs e)
        {
            MessageBox.Show("An fatal error has be found, please see the error.log file for more informations.");
            var ex = default(Exception);
            ex = e.Exception;
            var log = LogManager.GetLogger(typeof (Program)); //Log4NET
            log.Error("##### FORM EXCEPTION (version=" + UpdateManager.Version + "): #####\r\n" + ex.Message + "\r\n" +
                      ex.StackTrace + "\r\n" + ex.Source + "\r\n" + ex + "\r\n" + ex.Data + "\r\n" + ex.InnerException +
                      "\r\n" + ex.TargetSite);
        }

        public void UpdateKeyboard(object o, EventArgs args)
        {
            if (!_keyboardInitialized)
            {
                KeyboardHook.Instance.RegisterKeyboardHook();
                _keyboardInitialized = true;
            }
            else
            {
                KeyboardHook.Instance.SetHotkeys(TeraWindow.IsTeraActive());
            }
        }

        public void Update(long nintervalvalue, long ntotalDamage, Dictionary<Entity, EntityInfo> nentities,
            List<PlayerInfo> nstats)
        {
            UpdateUi changeUi =
                delegate(long intervalvalue, long totalDamage, Dictionary<Entity, EntityInfo> entities,
                    List<PlayerInfo> stats)
                {
                    StayTopMost();
                    var entitiesStats = entities.ToList().OrderByDescending(e => e.Value.LastHit).ToList();
                    var encounterList = new LinkedList<Entity>();
                    foreach (var entityStats in entitiesStats)
                    {
                        encounterList.AddLast(entityStats.Key);
                    }
                    UpdateComboboxEncounter(encounterList);
                    _entityStats.Update(entities);
                    var visiblePlayerStats = new HashSet<PlayerInfo>();
                    var counter = 0;
                    foreach (var playerStats in stats)
                    {
                        PlayerStats playerStatsControl;
                        Controls.TryGetValue(playerStats, out playerStatsControl);
                        if (playerStats.Dealt.Damage == 0 && playerStats.Received.Hits == 0)
                        {
                            continue;
                        }
                        visiblePlayerStats.Add(playerStats);
                        if (playerStatsControl != null) continue;
                        playerStatsControl = new PlayerStats(playerStats);
                        Controls.Add(playerStats, playerStatsControl);

                        if (counter == 9)
                        {
                            break;
                        }
                        counter++;
                    }

                    var invisibleControls = Controls.Where(x => !visiblePlayerStats.Contains(x.Key)).ToList();
                    foreach (var invisibleControl in invisibleControls)
                    {
                        Controls[invisibleControl.Key].CloseSkills();
                        Controls.Remove(invisibleControl.Key);
                    }

                    TotalDamage.Content = FormatHelpers.Instance.FormatValue(totalDamage);
                    var interval = TimeSpan.FromSeconds(intervalvalue);
                    Timer.Content = interval.ToString(@"mm\:ss");

                    Players.Items.Clear();
                    var sortedDict = from entry in Controls
                        orderby
                            stats[stats.IndexOf(entry.Value.PlayerInfo)].Dealt.DamageFraction(totalDamage) descending
                        select entry;
                    foreach (var item in sortedDict)
                    {
                        Players.Items.Add(item.Value);
                        var data = stats.IndexOf(item.Value.PlayerInfo);
                        item.Value.Repaint(stats[data], totalDamage, Width);
                    }
                    Height = Controls.Count*29 + CloseMeter.ActualHeight;
                };
            Dispatcher.Invoke(changeUi, nintervalvalue, ntotalDamage, nentities, nstats);
        }


        public void HandleConnected(string serverName)
        {
            ChangeTitle changeTitle = delegate(string newServerName) { Title = newServerName; };
            Dispatcher.Invoke(changeTitle, serverName);
        }

        public void SelectEncounter(Entity entity)
        {
            UpdateEncounter changeSelected = delegate(Entity newentity)
            {
                if (!newentity.IsBoss()) return;

                foreach (var item in ListEncounter.Items)
                {
                    var encounter = (Entity) ((ComboBoxItem) item).Content;
                    if (encounter != newentity) continue;
                    ListEncounter.SelectedItem = item;
                    NetworkController.Instance.ForceUpdate();
                    return;
                }
            };
            Dispatcher.Invoke(changeSelected, entity);
        }

        private void StayTopMost()

        {
            if (!Topmost) return;
            Topmost = false;
            Topmost = true;
        }

        private void UpdateComboboxEncounter(IEnumerable<Entity> entities)
        {
            Entity selectedEntity = null;
            if ((ComboBoxItem) ListEncounter.SelectedItem != null)
            {
                selectedEntity = (Entity) ((ComboBoxItem) ListEncounter.SelectedItem).Content;
            }

            ListEncounter.Items.Clear();
            ListEncounter.Items.Add(new ComboBoxItem {Content = new Entity("TOTAL")});
            var selected = false;
            foreach (var entity in entities)
            {
                var item = new ComboBoxItem {Content = entity};
                if (entity.IsBoss())
                {
                    item.Foreground = Brushes.Red;
                }
                ListEncounter.Items.Add(item);
                if (entity != selectedEntity) continue;
                ListEncounter.SelectedItem = ListEncounter.Items[ListEncounter.Items.Count - 1];
                selected = true;
            }
            if (selected) return;
            ListEncounter.SelectedItem = ListEncounter.Items[0];
        }

        private void MainWindow_OnClosed(object sender, EventArgs e)
        {
            BasicTeraData.Instance.WindowData.Location = new Point(Left, Top);
            NetworkController.Instance.Exit();
        }

        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            if (BasicTeraData.Instance.WindowData.RememberPosition)
            {
                Top = BasicTeraData.Instance.WindowData.Location.Y;
                Left = BasicTeraData.Instance.WindowData.Location.X;
                return;
            }
            Top = 0;
            Left = 0;
        }

        private void Button_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MainWindow_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                DragMove();
            }
            catch
            {
                Console.WriteLine(@"Exception Move");
            }
        }


        private void ToggleTopMost_OnClick(object sender, RoutedEventArgs e)
        {
            if (Topmost)
            {
                Topmost = false;
                PinImage.Source = BasicTeraData.Instance.PinData.Pin.Source;
                return;
            }
            Topmost = true;
            PinImage.Source = BasicTeraData.Instance.PinData.UnPin.Source;
        }


        private void ListEncounter_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count != 1) return;
            var encounter = (Entity) ((ComboBoxItem) e.AddedItems[0]).Content;
            if (encounter.Name.Equals("TOTAL"))
            {
                encounter = null;
            }
            if (encounter != NetworkController.Instance.Encounter)
            {
                NetworkController.Instance.Encounter = encounter;
                NetworkController.Instance.ForceUpdate();
            }
        }

        private void EntityStatsImage_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _entityStats.Show();
            _entityStats.Topmost = false;
            _entityStats.Topmost = true;
        }

        public void CloseEntityStats()
        {
            _entityStats.Hide();
            _entityStats.Topmost = false;
        }

        private delegate void UpdateEncounter(Entity entity);

        private delegate void UpdateUi(
            long intervalvalue, long totalDamage, Dictionary<Entity, EntityInfo> entities, List<PlayerInfo> stats);

        private delegate void ChangeTitle(string servername);
    }
}