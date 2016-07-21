using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Microsoft.AspNetCore.Mvc.Razor.Precompilation
{
    public class Program
    {
        private readonly static Type ProgramType = typeof(Program);

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

            var app = new PrecompilationApplication(ProgramType);

            EnsureValidDispatchRecipient(ref args);

            PrecompileCommandBase.Register<PrecompileRunCommand>(app);

            return app.Execute(args);
        }

        private static void EnsureValidDispatchRecipient(ref string[] args)
        {
            const string DispatcherVersionArgumentName = "--dispatcher-version";

            if (!args.Contains(DispatcherVersionArgumentName, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            var dispatcherArgumentIndex = Array.FindIndex(
                args,
                (value) => string.Equals(value, DispatcherVersionArgumentName, StringComparison.OrdinalIgnoreCase));
            var dispatcherArgumentValueIndex = dispatcherArgumentIndex + 1;
            if (dispatcherArgumentValueIndex < args.Length)
            {
                var dispatcherVersion = args[dispatcherArgumentValueIndex];

                var thisAssembly = ProgramType.GetTypeInfo().Assembly;
                var version = thisAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                    ?? thisAssembly.GetName().Version.ToString();

                if (string.Equals(dispatcherVersion, version, StringComparison.Ordinal))
                {
                    // Remove dispatcher arguments from
                    var preDispatcherArgument = args.Take(dispatcherArgumentIndex);
                    var postDispatcherArgument = args.Skip(dispatcherArgumentIndex + 2);
                    var newProgramArguments = preDispatcherArgument.Concat(postDispatcherArgument);
                    args = newProgramArguments.ToArray();
                    return;
                }
            }

            var thisAssemblyName = typeof(Program).GetTypeInfo().Assembly.GetName().Name;

            // Could not validate the dispatcher version.
            throw new InvalidOperationException("Could not invoke tool");
        }
    }
}
