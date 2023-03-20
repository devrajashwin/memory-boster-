﻿using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Input;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Main View Model
    /// </summary>
    internal class MainViewModel : ViewModel, IDisposable
    {
        #region Fields

        private Computer _computer;
        private readonly IComputerService _computerService;
        private BackgroundWorker _monitorAppWorker;
        private BackgroundWorker _monitorComputerWorker;
        private DateTimeOffset _lastAutoOptimizationByInterval = DateTimeOffset.Now;
        private DateTimeOffset _lastAutoOptimizationByMemoryUsage = DateTimeOffset.Now;
        private string _selectedProcess;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="MainViewModel" /> class.
        /// </summary>
        /// <param name="computerService">Computer service</param>
        /// <param name="notificationService">Notification service</param>
        public MainViewModel(IComputerService computerService, INotificationService notificationService)
            : base(notificationService)
        {
            _computerService = computerService;

            // Commands
            AddProcessToExclusionListCommand = new RelayCommand<string>(AddProcessToExclusionList);
            OptimizeCommand = new RelayCommand(OptimizeAsync, () => CanOptimize);
            RemoveProcessFromExclusionListCommand = new RelayCommand<string>(RemoveProcessFromExclusionList);

            // Models
            Computer = new Computer();

            if (IsInDesignMode)
            {
                Computer.OperatingSystem.IsWindowsVistaOrAbove = true;
                Computer.OperatingSystem.IsWindowsXp64BitOrAbove = true;
            }
            else
            {
                Computer.OperatingSystem = _computerService.GetOperatingSystem();
                MonitorAsync();
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets a value indicating whether [always on top].
        /// </summary>
        /// <value>
        ///   <c>true</c> if [always on top]; otherwise, <c>false</c>.
        /// </value>
        public bool AlwaysOnTop
        {
            get { return Settings.AlwaysOnTop; }
            set
            {
                try
                {
                    IsBusy = true;

                    Settings.AlwaysOnTop = value;
                    Settings.Save();

                    RaisePropertyChanged();
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        /// <summary>
        /// Gets or sets the automatic optimization interval.
        /// </summary>
        /// <value>
        /// The automatic optimization interval.
        /// </value>
        public int AutoOptimizationInterval
        {
            get { return Settings.AutoOptimizationInterval; }
            set
            {
                try
                {
                    IsBusy = true;

                    _lastAutoOptimizationByInterval = DateTimeOffset.Now;

                    Settings.AutoOptimizationInterval = value;
                    Settings.Save();

                    RaisePropertyChanged();
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }
        
        /// <summary>
        /// Gets or sets the automatic optimization memory usage.
        /// </summary>
        /// <value>
        /// The automatic optimization memory usage.
        /// </value>
        public int AutoOptimizationMemoryUsage
        {
            get { return Settings.AutoOptimizationMemoryUsage; }
            set
            {
                try
                {
                    IsBusy = true;

                    _lastAutoOptimizationByMemoryUsage = DateTimeOffset.Now;

                    Settings.AutoOptimizationMemoryUsage = value;
                    Settings.Save();

                    RaisePropertyChanged();
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether [automatic update].
        /// </summary>
        /// <value>
        ///   <c>true</c> if [automatic update]; otherwise, <c>false</c>.
        /// </value>
        public bool AutoUpdate
        {
            get { return Settings.AutoUpdate; }
            set
            {
                try
                {
                    IsBusy = true;

                    Settings.AutoUpdate = value;
                    Settings.Save();

                    RaisePropertyChanged();
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance can optimize.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance can optimize; otherwise, <c>false</c>.
        /// </value>
        public bool CanOptimize
        {
            get { return MemoryAreas != Enums.Memory.Area.None; }
        }

        /// <summary>
        /// Gets or sets the computer.
        /// </summary>
        /// <value>
        /// The computer.
        /// </value>
        public Computer Computer
        {
            get { return _computer; }
            private set
            {
                _computer = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Sets the culture.
        /// </summary>
        /// <value>
        /// The culture.
        /// </value>
        public Enums.Culture Culture
        {
            get
            {
                return Localization.Culture;
            }
            set
            {
                try
                {
                    IsBusy = true;

                    Localization.Culture = value;

                    NotificationService.Initialize();

                    Settings.Culture = value;
                    Settings.Save();

                    RaisePropertyChanged();
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        /// <summary>
        /// Gets or sets the memory areas.
        /// </summary>
        /// <value>
        /// The memory areas.
        /// </value>
        public Enums.Memory.Area MemoryAreas
        {
            get
            {
                if (!Computer.OperatingSystem.HasCombinedPageList)
                    Settings.MemoryAreas &= ~Enums.Memory.Area.CombinedPageList;

                if (!Computer.OperatingSystem.HasModifiedPageList)
                    Settings.MemoryAreas &= ~Enums.Memory.Area.ModifiedPageList;

                if (!Computer.OperatingSystem.HasProcessesWorkingSet)
                    Settings.MemoryAreas &= ~Enums.Memory.Area.ProcessesWorkingSet;

                if (!Computer.OperatingSystem.HasStandbyList)
                {
                    Settings.MemoryAreas &= ~Enums.Memory.Area.StandbyList;
                    Settings.MemoryAreas &= ~Enums.Memory.Area.StandbyListLowPriority;
                }

                if (!Computer.OperatingSystem.HasSystemWorkingSet)
                    Settings.MemoryAreas &= ~Enums.Memory.Area.SystemWorkingSet;

                return Settings.MemoryAreas;
            }
            set
            {
                try
                {
                    IsBusy = true;

                    if ((Settings.MemoryAreas & value) != 0)
                        Settings.MemoryAreas &= ~value;
                    else
                        Settings.MemoryAreas |= value;

                    switch (value)
                    {
                        case Enums.Memory.Area.StandbyList:
                            if ((Settings.MemoryAreas & Enums.Memory.Area.StandbyListLowPriority) != 0)
                                Settings.MemoryAreas &= ~Enums.Memory.Area.StandbyListLowPriority;
                            break;

                        case Enums.Memory.Area.StandbyListLowPriority:
                            if ((Settings.MemoryAreas & Enums.Memory.Area.StandbyList) != 0)
                                Settings.MemoryAreas &= ~Enums.Memory.Area.StandbyList;
                            break;
                    }

                    Settings.Save();

                    RaisePropertyChanged();
                    RaisePropertyChanged(() => CanOptimize);
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether [minimize to tray when closed].
        /// </summary>
        /// <value>
        ///   <c>true</c> if [minimize to tray when closed]; otherwise, <c>false</c>.
        /// </value>
        public bool MinimizeToTrayWhenClosed
        {
            get { return Settings.MinimizeToTrayWhenClosed; }
            set
            {
                try
                {
                    IsBusy = true;

                    Settings.MinimizeToTrayWhenClosed = value;
                    Settings.Save();

                    RaisePropertyChanged();
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        /// <summary>
        /// Gets the processes.
        /// </summary>
        /// <value>
        /// The processes.
        /// </value>
        public ObservableCollection<string> Processes
        {
            get
            {
                var processes = new ObservableCollection<string>(Process.GetProcesses()
                    .Where(process => process != null && !Settings.ProcessExclusionList.Contains(process.ProcessName))
                    .Select(process => process.ProcessName.ToLower().Replace(".exe", string.Empty))
                    .Distinct()
                    .OrderBy(name => name));

                SelectedProcess = processes.FirstOrDefault();

                return processes;
            }
        }

        /// <summary>
        /// Gets or sets the process exclusion list.
        /// </summary>
        /// <value>
        /// The process exclusion list.
        /// </value>
        public ObservableCollection<string> ProcessExclusionList
        {
            get
            {
                return new ObservableCollection<string>(Settings.ProcessExclusionList);
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether [run on startup].
        /// </summary>
        /// <value>
        ///   <c>true</c> if [run on startup]; otherwise, <c>false</c>.
        /// </value>
        public bool RunOnStartup
        {
            get { return Settings.RunOnStartup; }
            set
            {
                try
                {
                    IsBusy = true;

                    Settings.RunOnStartup = value;
                    Settings.Save();

                    RaisePropertyChanged();
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected process.
        /// </summary>
        /// <value>
        /// The selected process.
        /// </value>
        public string SelectedProcess
        {
            get { return _selectedProcess; }
            set
            {
                _selectedProcess = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether [show optimization notifications].
        /// </summary>
        /// <value>
        ///   <c>true</c> if [show optimization notifications]; otherwise, <c>false</c>.
        /// </value>
        public bool ShowOptimizationNotifications
        {
            get { return Settings.ShowOptimizationNotifications; }
            set
            {
                try
                {
                    IsBusy = true;

                    Settings.ShowOptimizationNotifications = value;
                    Settings.Save();

                    RaisePropertyChanged();
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether [start minimized].
        /// </summary>
        /// <value>
        ///   <c>true</c> if [start minimized]; otherwise, <c>false</c>.
        /// </value>
        public bool StartMinimized
        {
            get { return Settings.StartMinimized; }
            set
            {
                try
                {
                    IsBusy = true;

                    Settings.StartMinimized = value;
                    Settings.Save();

                    RaisePropertyChanged();
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        /// <summary>
        /// Gets the title.
        /// </summary>
        /// <value>
        /// The title.
        /// </value>
        public static string Title
        {
            get
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;

                return string.Format(CultureInfo.CurrentCulture, "{0} {1}.{2}", Constants.App.Title, version.Major, version.Minor);
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_monitorAppWorker != null)
                {

                    try
                    {
                        _monitorAppWorker.CancelAsync();
                    }
                    catch
                    {
                        // ignored
                    }

                    try
                    {
                        _monitorAppWorker.Dispose();
                    }
                    catch
                    {
                        // ignored
                    }
                }

                if (_monitorComputerWorker != null)
                {

                    try
                    {
                        _monitorComputerWorker.CancelAsync();
                    }
                    catch
                    {
                        // ignored
                    }

                    try
                    {
                        _monitorComputerWorker.Dispose();
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Commands

        /// <summary>
        /// Gets the add process to exclusion list.
        /// </summary>
        /// <value>
        /// The add process to exclusion list.
        /// </value>
        public ICommand AddProcessToExclusionListCommand { get; private set; }

        /// <summary>
        /// Gets the optimize command.
        /// </summary>
        /// <value>
        /// The optimize command.
        /// </value>
        public ICommand OptimizeCommand { get; private set; }

        /// <summary>
        /// Gets the remove process from exclusion list command.
        /// </summary>
        /// <value>
        /// The remove process from exclusion list command.
        /// </value>
        public ICommand RemoveProcessFromExclusionListCommand { get; private set; }

        #endregion

        #region Methods

        /// <summary>
        /// Adds the process to exclusion list.
        /// </summary>
        /// <param name="process">The process.</param>
        private void AddProcessToExclusionList(string process)
        {
            try
            {
                IsBusy = true;

                if (!Settings.ProcessExclusionList.Contains(process) && !string.IsNullOrWhiteSpace(process))
                {
                    Settings.ProcessExclusionList.Add(process);
                    Settings.Save();

                    RaisePropertyChanged(() => Processes);
                    RaisePropertyChanged(() => ProcessExclusionList);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Monitor App Resources
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="DoWorkEventArgs" /> instance containing the event data.</param>
        private void MonitorApp(object sender, DoWorkEventArgs e)
        {
            while (!_monitorAppWorker.CancellationPending)
            {
                try
                {
                    // Check if it's busy
                    if (IsBusy)
                        continue;

                    // Update app
                    if (Settings.AutoUpdate)
                        App.Update();

                    // Auto Optimization
                    if (CanOptimize)
                    {
                        // Interval
                        if (Settings.AutoOptimizationInterval > 0 &&
                            DateTimeOffset.Now.Subtract(_lastAutoOptimizationByInterval).TotalHours >= Settings.AutoOptimizationInterval)
                        {
                            OptimizeAsync();

                            _lastAutoOptimizationByInterval = DateTimeOffset.Now;
                        }
                        else
                        {
                            // Memory usage
                            if (Settings.AutoOptimizationMemoryUsage > 0 &&
                                Computer.Memory.FreePercentage < Settings.AutoOptimizationMemoryUsage &&
                                DateTimeOffset.Now.Subtract(_lastAutoOptimizationByMemoryUsage).TotalMinutes >= Constants.App.AutoOptimizationMemoryUsageInterval)
                            {
                                OptimizeAsync();

                                _lastAutoOptimizationByMemoryUsage = DateTimeOffset.Now;
                            }
                        }
                    }

                    // Delay
                    Thread.Sleep(60000);
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex.GetBaseException().Message);
                }
            }
        }

        /// <summary>
        /// Monitor Background Tasks
        /// </summary>
        private void MonitorAsync()
        {
            // Monitor App Resources
            try
            {
                using (_monitorAppWorker = new BackgroundWorker())
                {
                    _monitorAppWorker.DoWork += MonitorApp;
                    _monitorAppWorker.WorkerSupportsCancellation = true;
                    _monitorAppWorker.RunWorkerAsync();
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }

            // Monitor Computer Resources
            try
            {
                using (_monitorComputerWorker = new BackgroundWorker())
                {
                    _monitorComputerWorker.DoWork += MonitorComputer;
                    _monitorComputerWorker.WorkerSupportsCancellation = true;
                    _monitorComputerWorker.RunWorkerAsync();
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        /// <summary>
        /// Monitor Computer Resources
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="DoWorkEventArgs" /> instance containing the event data.</param>
        private void MonitorComputer(object sender, DoWorkEventArgs e)
        {
            while (!_monitorComputerWorker.CancellationPending)
            {
                try
                {
                    // Check if it's busy
                    if (IsBusy)
                        continue;

                    // Update memory info
                    Computer.Memory = _computerService.GetMemory();
                    RaisePropertyChanged(() => Computer);

                    // Delay
                    Thread.Sleep(5000);
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex.GetBaseException().Message);
                }
            }
        }

        /// <summary>
        /// Optimize
        /// </summary>
        private void OptimizeAsync()
        {
            try
            {
                using (BackgroundWorker worker = new BackgroundWorker())
                {
                    worker.DoWork += Optimize;
                    worker.RunWorkerAsync();
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        /// <summary>
        /// Optimize
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="DoWorkEventArgs" /> instance containing the event data.</param>
        private void Optimize(object sender, DoWorkEventArgs e)
        {
            try
            {
                IsBusy = true;

                // Memory clean
                _computerService.CleanMemory(Settings.MemoryAreas);

                // Update memory info
                Computer.Memory = _computerService.GetMemory();
                RaisePropertyChanged(() => Computer);

                // Notification
                if (Settings.ShowOptimizationNotifications)
                    Notify(Localization.MemoryOptimized);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Removes the process from exclusion list.
        /// </summary>
        /// <param name="process">The process.</param>
        private void RemoveProcessFromExclusionList(string process)
        {
            try
            {
                IsBusy = true;

                if (Settings.ProcessExclusionList.Remove(process))
                {
                    Settings.Save();

                    RaisePropertyChanged(() => Processes);
                    RaisePropertyChanged(() => ProcessExclusionList);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion
    }
}
