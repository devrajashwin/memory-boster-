﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Computer Service
    /// </summary>
    public class ComputerService : IComputerService
    {
        #region Fields

        private Memory _memory = new Memory(new Structs.Windows.MemoryStatusEx());
        private OperatingSystem _operatingSystem;

        #endregion Fields

        #region Properties

        /// <summary>
        /// Gets the memory info (RAM)
        /// </summary>
        public Memory Memory
        {
            get
            {
                try
                {
                    var memoryStatusEx = new Structs.Windows.MemoryStatusEx();

                    if (!NativeMethods.GlobalMemoryStatusEx(memoryStatusEx))
                        Logger.Error(new Win32Exception(Marshal.GetLastWin32Error()));

                    _memory = new Memory(memoryStatusEx);
                }
                catch (Exception e)
                {
                    Logger.Error(e.Message);
                }

                return _memory;
            }
        }

        /// <summary>
        /// Gets the operating system info
        /// </summary>
        public OperatingSystem OperatingSystem
        {
            get
            {
                if (_operatingSystem == null)
                {
                    var operatingSystem = Environment.OSVersion;

                    _operatingSystem = new OperatingSystem
                    {
                        Is64Bit = Environment.Is64BitOperatingSystem,
                        IsWindows8OrGreater = operatingSystem.Version.Major >= 6.2,
                        IsWindowsVistaOrGreater = operatingSystem.Version.Major >= 6,
                        IsWindowsXpOrGreater = operatingSystem.Version.Major >= 5.1
                    };
                }

                return _operatingSystem;
            }
        }

        #endregion Properties

        #region Events

        /// <summary>
        /// Occurs when [optimize progress is update].
        /// </summary>
        public event Action<byte, string> OnOptimizeProgressUpdate;

        #endregion Events

        #region Methods (Computer)

        /// <summary>
        /// Increase the Privilege using a privilege name
        /// </summary>
        /// <param name="privilegeName">The name of the privilege that needs to be increased</param>
        /// <returns></returns>
        private bool SetIncreasePrivilege(string privilegeName)
        {
            var result = false;

            using (var current = WindowsIdentity.GetCurrent(TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges))
            {
                Structs.Windows.TokenPrivileges newState;
                newState.Count = 1;
                newState.Luid = 0L;
                newState.Attr = Constants.Windows.PrivilegeAttribute.Enabled;

                // Retrieves the uid used on a specified system to locally represent the specified privilege name
                if (NativeMethods.LookupPrivilegeValue(null, privilegeName, ref newState.Luid))
                {
                    // Enables or disables privileges in a specified access token
                    result = NativeMethods.AdjustTokenPrivileges(current.Token, false, ref newState, 0, IntPtr.Zero, IntPtr.Zero);
                }

                if (!result)
                    Logger.Error(new Win32Exception(Marshal.GetLastWin32Error()));
            }

            return result;
        }

        #endregion Methods (Computer)

        #region Methods (Memory)

        /// <summary>
        /// Optimize the computer
        /// </summary>
        /// <param name="areas">Memory areas</param>
        public async Task Optimize(Enums.Memory.Areas areas)
        {
            //await Task.Run(() => OptimizeCombinedPageList());
            //await Task.Run(() => OptimizeModifiedPageList());
            //await Task.Run(() => OptimizeProcessesWorkingSet());
            //await Task.Run(() => OptimizeStandbyList());
            //await Task.Run(() => OptimizeSystemWorkingSet());
            var task1 = Task.Factory.StartNew(() => OptimizeCombinedPageList());
            var task2 = Task.Factory.StartNew(() => OptimizeModifiedPageList());
            var task3 = Task.Factory.StartNew(() => OptimizeProcessesWorkingSet());
            var task4 = Task.Factory.StartNew(() => OptimizeStandbyList());
            var task5 = Task.Factory.StartNew(() => OptimizeSystemWorkingSet());
        }

        /// <summary>
        /// Optimize the combined page list.
        /// </summary>
        /// <exception cref="Win32Exception"></exception>
        private void OptimizeCombinedPageList()
        {
            // Windows minimum version
            if (!OperatingSystem.HasCombinedPageList)
                throw new Exception(string.Format(Localizer.Culture, Localizer.String.ErrorMemoryAreaOptimizationNotSupported, Localizer.String.MemoryCombinedPageList));

            // Check privilege
            if (!SetIncreasePrivilege(Constants.Windows.Privilege.SeProfSingleProcessName))
                throw new Exception(string.Format(Localizer.Culture, Localizer.String.ErrorAdminPrivilegeRequired, Constants.Windows.Privilege.SeProfSingleProcessName));

            var handle = GCHandle.Alloc(0);

            try
            {
                var memoryCombineInformationEx = new Structs.Windows.MemoryCombineInformationEx();

                handle = GCHandle.Alloc(memoryCombineInformationEx, GCHandleType.Pinned);

                var length = Marshal.SizeOf(memoryCombineInformationEx);

                if (NativeMethods.NtSetSystemInformation(Constants.Windows.SystemInformationClass.SystemCombinePhysicalMemoryInformation, handle.AddrOfPinnedObject(), length) != Constants.Windows.SystemErrorCode.ErrorSuccess)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                try
                {
                    if (handle.IsAllocated)
                        handle.Free();
                }
                catch (InvalidOperationException)
                {
                    // ignored
                }
            }
        }

        /// <summary>
        /// Optimize the modified page list.
        /// </summary>
        /// <exception cref="Win32Exception">
        /// </exception>
        private void OptimizeModifiedPageList()
        {
            // Windows minimum version
            if (!OperatingSystem.HasModifiedPageList)
                throw new Exception(string.Format(Localizer.Culture, Localizer.String.ErrorMemoryAreaOptimizationNotSupported, Localizer.String.MemoryModifiedPageList));

            // Check privilege
            if (!SetIncreasePrivilege(Constants.Windows.Privilege.SeProfSingleProcessName))
                throw new Exception(string.Format(Localizer.Culture, Localizer.String.ErrorAdminPrivilegeRequired, Constants.Windows.Privilege.SeProfSingleProcessName));

            var handle = GCHandle.Alloc(Constants.Windows.SystemMemoryListCommand.MemoryFlushModifiedList, GCHandleType.Pinned);

            try
            {
                if (NativeMethods.NtSetSystemInformation(Constants.Windows.SystemInformationClass.SystemMemoryListInformation, handle.AddrOfPinnedObject(), Marshal.SizeOf(Constants.Windows.SystemMemoryListCommand.MemoryFlushModifiedList)) != Constants.Windows.SystemErrorCode.ErrorSuccess)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                try
                {
                    if (handle.IsAllocated)
                        handle.Free();
                }
                catch (InvalidOperationException)
                {
                    // ignored
                }
            }
        }

        /// <summary>
        /// Optimize the processes working set.
        /// </summary>
        /// <exception cref="Win32Exception"></exception>
        private void OptimizeProcessesWorkingSet()
        {
            // Windows minimum version
            if (!OperatingSystem.HasProcessesWorkingSet)
                throw new Exception(string.Format(Localizer.Culture, Localizer.String.ErrorMemoryAreaOptimizationNotSupported, Localizer.String.MemoryProcessesWorkingSet));

            // Check privilege
            if (!SetIncreasePrivilege(Constants.Windows.Privilege.SeDebugName))
                throw new Exception(string.Format(Localizer.Culture, Localizer.String.ErrorAdminPrivilegeRequired, Constants.Windows.Privilege.SeDebugName));

            var errors = new StringBuilder();
            var processes = Process.GetProcesses().Where(process => process != null && !Settings.ProcessExclusionList.Contains(process.ProcessName));

            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        if (!NativeMethods.EmptyWorkingSet(process.Handle))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    catch (InvalidOperationException)
                    {
                        // ignored
                    }
                    catch (Win32Exception e)
                    {
                        if (e.NativeErrorCode != Constants.Windows.SystemErrorCode.ErrorAccessDenied)
                            errors.Append(string.Format(Localizer.Culture, "{0}: {1} | ", process.ProcessName, e.GetMessage()));
                    }
                }
            }

            if (errors.Length > 3)
            {
                errors.Remove(errors.Length - 3, 3);
                throw new Exception(errors.ToString());
            }
        }

        /// <summary>
        /// Optimize the standby list.
        /// </summary>
        /// <param name="lowPriority">if set to <c>true</c> [low priority].</param>
        /// <exception cref="Win32Exception"></exception>
        /// <exception cref="Win32Exception"></exception>
        private void OptimizeStandbyList(bool lowPriority = false)
        {
            // Windows minimum version
            if (!OperatingSystem.HasStandbyList)
                throw new Exception(string.Format(Localizer.Culture, Localizer.String.ErrorMemoryAreaOptimizationNotSupported, Localizer.String.MemoryStandbyList));

            // Check privilege
            if (!SetIncreasePrivilege(Constants.Windows.Privilege.SeProfSingleProcessName))
                throw new Exception(string.Format(Localizer.Culture, Localizer.String.ErrorAdminPrivilegeRequired, Constants.Windows.Privilege.SeProfSingleProcessName));

            object memoryPurgeStandbyList = lowPriority ? Constants.Windows.SystemMemoryListCommand.MemoryPurgeLowPriorityStandbyList : Constants.Windows.SystemMemoryListCommand.MemoryPurgeStandbyList;
            var handle = GCHandle.Alloc(memoryPurgeStandbyList, GCHandleType.Pinned);

            try
            {
                if (NativeMethods.NtSetSystemInformation(Constants.Windows.SystemInformationClass.SystemMemoryListInformation, handle.AddrOfPinnedObject(), Marshal.SizeOf(memoryPurgeStandbyList)) != Constants.Windows.SystemErrorCode.ErrorSuccess)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                try
                {
                    if (handle.IsAllocated)
                        handle.Free();
                }
                catch (InvalidOperationException)
                {
                    // ignored
                }
            }
        }

        /// <summary>
        /// Optimize the system working set.
        /// </summary>
        /// <exception cref="Win32Exception">
        /// </exception>
        /// <exception cref="Win32Exception"></exception>
        private void OptimizeSystemWorkingSet()
        {
            // Windows minimum version
            if (!OperatingSystem.HasSystemWorkingSet)
                throw new Exception(string.Format(Localizer.Culture, Localizer.String.ErrorMemoryAreaOptimizationNotSupported, Localizer.String.MemorySystemWorkingSet));

            // Check privilege
            if (!SetIncreasePrivilege(Constants.Windows.Privilege.SeIncreaseQuotaName))
                throw new Exception(string.Format(Localizer.Culture, Localizer.String.ErrorAdminPrivilegeRequired, Constants.Windows.Privilege.SeIncreaseQuotaName));

            var handle = GCHandle.Alloc(0);

            try
            {
                object systemCacheInformation;

                if (OperatingSystem.Is64Bit)
                    systemCacheInformation = new Structs.Windows.SystemCacheInformation64 { MinimumWorkingSet = -1L, MaximumWorkingSet = -1L };
                else
                    systemCacheInformation = new Structs.Windows.SystemCacheInformation32 { MinimumWorkingSet = uint.MaxValue, MaximumWorkingSet = uint.MaxValue };

                handle = GCHandle.Alloc(systemCacheInformation, GCHandleType.Pinned);

                var length = Marshal.SizeOf(systemCacheInformation);

                if (NativeMethods.NtSetSystemInformation(Constants.Windows.SystemInformationClass.SystemFileCacheInformation, handle.AddrOfPinnedObject(), length) != Constants.Windows.SystemErrorCode.ErrorSuccess)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                try
                {
                    if (handle.IsAllocated)
                        handle.Free();
                }
                catch (InvalidOperationException)
                {
                    // ignored
                }
            }

            var fileCacheSize = IntPtr.Subtract(IntPtr.Zero, 1); // Flush

            if (!NativeMethods.SetSystemFileCacheSize(fileCacheSize, fileCacheSize, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        #endregion Methods (Memory)
    }
}