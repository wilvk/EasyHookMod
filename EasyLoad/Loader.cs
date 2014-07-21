﻿using System;
using System.Text;
using RGiesecke.DllExport;
using System.Runtime.InteropServices;
using System.Reflection;
using System.IO;

namespace EasyLoad
{
    /// <summary>
    /// Proxy class for calling the EasyHook injection loader within the child AppDomain
    /// </summary>
    public class LoadEasyHookProxy : MarshalByRefObject
    {
        /// <summary>
        /// Call EasyHook.InjectionLoader.Main within the child AppDomain
        /// </summary>
        /// <param name="inParam"></param>
        /// <returns>0 for success, -1 for fail</returns>
        public int Load(String inParam)
        {
            return EasyHook.InjectionLoader.Main(inParam);
        }
    }

    /// <summary>
    /// Static EasyHook Loader class
    /// </summary>
    public static class Loader
    {
        static object _lock = new object();
        static AppDomain _easyHookDomain;
        static Loader()
        {
            // Custom AssemblyResolve is necessary to load assembly from same directory as EasyLoad
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.AssemblyResolve += new ResolveEventHandler(LoadFromSameFolder);
        }

        /// <summary>
        /// Custom AssemblyResolve is necessary to load assembly from same directory as EasyLoad
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        static Assembly LoadFromSameFolder(object sender, ResolveEventArgs args)
        {
            string folderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string assemblyPath = Path.Combine(folderPath, new AssemblyName(args.Name).Name + ".dll");
            if (File.Exists(assemblyPath) == false) return null;
            Assembly assembly = Assembly.LoadFrom(assemblyPath);
            return assembly;
        }

        /// <summary>
        /// Loads EasyHook and commences the loading of user supplied assembly. This method is exported as a DllExport, making it consumable from native code.
        /// </summary>
        /// <param name="inParam"></param>
        /// <returns>0 for success, -1 for fail</returns>
        [DllExport("Load", System.Runtime.InteropServices.CallingConvention.StdCall)]
        public static int Load([MarshalAs(UnmanagedType.LPWStr)]String inParam)
        {
            lock (_lock)
            {
                try
                {
                    if (_easyHookDomain == null)
                    {
                        // Evidence of null means use the current appdomain evidence
                        _easyHookDomain = AppDomain.CreateDomain("EasyHook", null, new AppDomainSetup()
                        {
                            ApplicationBase = Path.GetDirectoryName(typeof(Loader).Assembly.Location),
                            // ShadowCopyFiles = "true", // copies assemblies from ApplicationBase into cache, leaving originals unlocked and updatable
                        });
                    }

                    // Load the EasyHook assembly in the child AppDomain by using the proxy class
                    Type proxyType = typeof(LoadEasyHookProxy);
                    if (proxyType.Assembly != null)
                    {
                        // This is where the currentDomain.AssemblyResolve that we setup within the static 
                        // constructor is required.
                        var proxy = (LoadEasyHookProxy)_easyHookDomain.CreateInstanceFrom(
                                        proxyType.Assembly.Location,
                                        proxyType.FullName).Unwrap();

                        // Loads EasyHook.dll into the AppDomain, which in turn loads the target assembly
                        return proxy.Load(inParam);
                    }
                }
                catch (Exception)
                {
                }

                // Failed
                return -1;
            }
        }

        /// <summary>
        /// Attempts to terminate the AppDomain that is hosting the injected assembly. This method is exported as a DllExport, making it consumable from native code.
        /// </summary>
        [DllExport("Close", System.Runtime.InteropServices.CallingConvention.StdCall)]
        public static void Close()
        {
            lock (_lock)
            {
                if (_easyHookDomain != null)
                {
                    try
                    {
                        // Unload the AppDomain
                        AppDomain.Unload(_easyHookDomain);
                    }
                    catch (System.CannotUnloadAppDomainException)
                    {
                        // Usually means that one or more threads within the AppDomain haven't finished exiting (e.g. still within a finalize)
                        var i = 0;
                        while (i < 3) // try up to 3 times to unload the AppDomain
                        {
                            System.Threading.Thread.Sleep(1000);
                            try
                            {
                                AppDomain.Unload(_easyHookDomain);
                            }
                            catch
                            {
                                i++;
                                continue;
                            }
                            break;
                        }
                    }
                }
                _easyHookDomain = null;
            }
        }
    }
}
