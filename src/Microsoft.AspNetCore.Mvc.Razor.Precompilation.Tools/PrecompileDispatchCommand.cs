// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.InternalAbstractions;
using Microsoft.DotNet.ProjectModel;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Internal;
using NuGet.Frameworks;

namespace Microsoft.AspNetCore.Mvc.Razor.Precompilation.Tools
{
    public class PrecompileDispatchCommand : PrecompileCommandBase
    {
        private CommandOption NoBuildOption { get; set; }

        protected override void Configure(CommandLineApplication app)
        {
            base.Configure(app);
            NoBuildOption = app.Option("--no-build", "Do not build project before compiling views.", CommandOptionType.NoValue);
        }

        protected override int ExecuteCore()
        {
            if (!NoBuildOption.HasValue())
            {
                var exitCode = BuildProject();
                if (exitCode != 0)
                {
                    return exitCode;
                }
            }

            var dispatchArgs = new List<string>
            {
                ProjectPath,
                "--framework",
                FrameworkOption.Value(),
                "--configuration",
                ConfigurationOption.Value(),
            };

            string outputPath = null;
            if (OutputPathOption.HasValue())
            {
                outputPath = OutputPathOption.Value();

                dispatchArgs.Add("--output-path");
                dispatchArgs.Add(outputPath);
            }

            if (ConfigureCompilationType.HasValue())
            {
                dispatchArgs.Add(ConfigureCompilationType.Template);
                dispatchArgs.Add(ConfigureCompilationType.Value());
            }

            var toolName = typeof(Precompilation.Program).GetTypeInfo().Assembly.GetName().Name;
            var dispatchCommand = DotnetToolDispatcher.CreateDispatchCommand(
                dispatchArgs,
                NuGetFramework.Parse(FrameworkOption.Value()),
                ConfigurationOption.Value(),
                outputPath: outputPath,
                buildBasePath: null,
                projectDirectory: ProjectPath,
                toolName: toolName);

            var commandExitCode = dispatchCommand
                .ForwardStdErr(Console.Error)
                .ForwardStdOut(Console.Out)
                .Execute()
                .ExitCode;

            return commandExitCode;
        }

        private int BuildProject()
        {
            var workspace = new BuildWorkspace(ProjectReaderSettings.ReadFromEnvironment());
            
            var projectContext = workspace.GetProjectContext(ProjectPath, NuGetFramework.Parse(FrameworkOption.Value()));
            if (projectContext == null)
            {
                Console.Error.WriteLine($"Project '{ProjectPath}' does not support framework: {FrameworkOption.Value()}");
                return 1;
            }

            projectContext = workspace.GetRuntimeContext(
                projectContext, 
                RuntimeEnvironmentRidExtensions.GetAllCandidateRuntimeIdentifiers());

            var arguments = new List<string>
            {
                ProjectPath,
                "--framework",
                FrameworkOption.Value(),
                "--configuration",
                ConfigurationOption.Value(),
            };

            if (OutputPathOption.HasValue())
            {
                arguments.Add("--output");
                arguments.Add(OutputPathOption.Value());
            }

            return Command.CreateDotNet("build", arguments, NuGetFramework.Parse(FrameworkOption.Value()), ConfigurationOption.Value())
                .ForwardStdErr(Console.Error)
                .ForwardStdOut(Console.Out)
                .Execute()
                .ExitCode;
        }
    }
}
