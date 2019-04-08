// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Extensions.Hosting.WindowsServices
{
    public class ServiceHelpers
    {
        public static bool IsWindowsService()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            var parent = Internal.Win32.GetParentProcess();
            if (parent == null)
            {
                return false;
            }
            return parent.SessionId == 0 && string.Equals("services", parent.ProcessName, StringComparison.OrdinalIgnoreCase);
        }
    }
}

