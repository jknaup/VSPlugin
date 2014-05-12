﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using RIM.VSNDK_Package.Diagnostics;

namespace RIM.VSNDK_Package.Model
{
    /// <summary>
    /// Descriptor of a locally installed NDK.
    /// </summary>
    internal sealed class NdkInfo : ApiInfo, IComparable<NdkInfo>
    {
        public NdkInfo(string name, Version version, string hostPath, string targetPath, DeviceInfo[] devices)
            : base(name, version)
        {
            if (string.IsNullOrEmpty(hostPath))
                throw new ArgumentNullException("hostPath");
            if (string.IsNullOrEmpty(targetPath))
                throw new ArgumentNullException("targetPath");
            if (devices == null)
                throw new ArgumentNullException("devices");

            HostPath = hostPath;
            TargetPath = targetPath;
            Devices = devices;
        }

        #region Properties

        public string HostPath
        {
            get;
            private set;
        }

        public string TargetPath
        {
            get;
            private set;
        }

        public DeviceInfo[] Devices
        {
            get;
            private set;
        }

        #endregion

        /// <summary>
        /// Checks if given NDK is really available.
        /// </summary>
        public bool Exists()
        {
            try
            {
                return Directory.Exists(HostPath) && Directory.Exists(TargetPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Loads info about a single NDK from specific config file, given as XML.
        /// </summary>
        public static NdkInfo Load(XmlReader reader)
        {
            if (reader == null)
                return null;

            string name = null;
            Version version = null;
            string hostPath = null;
            string targetPath = null;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "name":
                            name = reader.ReadString();
                            break;
                        case "version":
                            version = new Version(reader.ReadString());
                            break;
                        case "host":
                            hostPath = reader.ReadString();
                            break;
                        case "target":
                            targetPath = reader.ReadString();
                            break;
                    }
                }

                if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "installation")
                {
                    if (!string.IsNullOrEmpty(targetPath) && !string.IsNullOrEmpty(hostPath))
                    {
                        const string DescriptorFileName = "blackberry-sdk-descriptor.xml";

                        var ndkDescriptor = Path.Combine(targetPath, DescriptorFileName);
                        DeviceInfo[] devices = LoadDevices(ndkDescriptor);

                        // if failed to load devices, try to find one folder above:
                        if (devices == null)
                        {
                            ndkDescriptor = Path.Combine(targetPath, "..", DescriptorFileName);
                            devices = LoadDevices(ndkDescriptor);
                        }

                        // or another above:
                        if (devices == null)
                        {
                            ndkDescriptor = Path.Combine(targetPath, "..", "..", DescriptorFileName);
                            devices = LoadDevices(ndkDescriptor);
                        }

                        // OK, give up, and say it's unknown:
                        if (devices == null)
                            devices = new DeviceInfo[0];

                        // try to define info about the installation:
                        return new NdkInfo(name, version, hostPath, targetPath, devices);
                    }

                    return null;
                }
            }

            return null;
        }

        private static DeviceInfo[] LoadDevices(string ndkDescriptorFileName)
        {
            try
            {
                if (!File.Exists(ndkDescriptorFileName))
                    return null;

                // try to load info about supported devices by the NDK:
                using (var fileReader = new StreamReader(ndkDescriptorFileName, Encoding.UTF8))
                {
                    using (var descReader = XmlReader.Create(fileReader))
                    {
                        return DeviceInfo.Load(descReader);
                    }
                }
            }
            catch (Exception ex)
            {
                TraceLog.WriteLine("Unable to load NDK descriptor file: \"{0}\"", ndkDescriptorFileName);
                TraceLog.WriteException(ex);

                return null;
            }
        }

        /// <summary>
        /// Loads info about all installed NDKs.
        /// </summary>
        public static NdkInfo[] Load(string globalNdkConfigFolder)
        {
            if (string.IsNullOrEmpty(globalNdkConfigFolder))
                throw new ArgumentNullException("globalNdkConfigFolder");

            var result = new List<NdkInfo>();

            // list all configuration files:
            if (Directory.Exists(globalNdkConfigFolder))
            {
                try
                {
                    var files = Directory.GetFiles(globalNdkConfigFolder, "*.xml", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            using (var fileReader = new StreamReader(file, Encoding.UTF8))
                            {
                                using (var reader = XmlReader.Create(fileReader))
                                {
                                    var info = Load(reader);
                                    if (info != null && info.Exists())
                                        result.Add(info);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            TraceLog.WriteLine("Unable to open configuration file: \"{0}\"", file);
                            TraceLog.WriteException(ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    TraceLog.WriteLine("Unable to load info about configuration files from folder: \"{0}\"", globalNdkConfigFolder);
                    TraceLog.WriteException(ex);
                }
            }

            result.Sort();
            return result.ToArray();
        }


        /// <summary>
        /// Compares the current object with another object of the same type.
        /// </summary>
        /// <returns>
        /// A value that indicates the relative order of the objects being compared. The return value has the following meanings: Value Meaning Less than zero This object is less than the <paramref name="other"/> parameter.Zero This object is equal to <paramref name="other"/>. Greater than zero This object is greater than <paramref name="other"/>. 
        /// </returns>
        /// <param name="other">An object to compare with this object.</param>
        public int CompareTo(NdkInfo other)
        {
            if (other == null)
                return 1;

            int cmp = Version.CompareTo(other.Version);
            if (cmp != 0)
                return cmp;

            return string.Compare(Name, other.Name, StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
