// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Microsoft.AspNetCore.Mvc.Razor.Precompilation.Tools
{
    public class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--debug")
            {
                args = args.Skip(1).ToArray();
                Console.WriteLine("Waiting for debugger. Process " + Process.GetCurrentProcess().Id);
                while (!Debugger.IsAttached)
                {
                    Thread.Sleep(50);
                }
            }

            var app = new PrecompilationApplication(typeof(Program));

            PrecompileCommandBase.Register<PrecompileDispatchCommand>(app);

            return app.Execute(args);
        }
    }
}
