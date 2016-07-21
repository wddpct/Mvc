// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Extensions.CommandLineUtils;

namespace Microsoft.AspNetCore.Mvc.Razor.Precompilation
{
    public abstract class PrecompileCommandBase
    {
        protected CommandOption OutputPathOption { get; set; }

        protected CommandArgument ProjectArgument { get; set; }

        protected CommandOption FrameworkOption { get; set; }

        protected CommandOption ConfigurationOption { get; set; }

        protected string ProjectPath { get; private set; }

        public static void Register<TPrecompileCommandBase>(CommandLineApplication app) where
            TPrecompileCommandBase : PrecompileCommandBase, new()
        {
            var command = new TPrecompileCommandBase();
            command.Configure(app);
        }

        protected virtual void Configure(CommandLineApplication app)
        {
            app.Description = "Precompiles an application.";
            app.HelpOption("-?|-h|--help");
            ProjectArgument = app.Argument(
                "project",
                "The path to the project (project folder or project.json) with precompilation.");
            FrameworkOption = app.Option(
                "-f|--framework",
                "Target Framework",
                CommandOptionType.SingleValue);
            OutputPathOption = app.Option(
                "-o|--output-path",
                "The output (bin or publish) directory.",
                CommandOptionType.SingleValue);

            ConfigurationOption = app.Option(
                "-c|--configuration", 
                "Configuration", 
                CommandOptionType.SingleValue);

            app.OnExecute((Func<int>)Execute);
        }

        protected int Execute()
        {
            if (!string.IsNullOrEmpty(ProjectArgument.Value))
            {
                ProjectPath = Path.GetFullPath(ProjectArgument.Value);
                if (ProjectPath.EndsWith("project.json", StringComparison.OrdinalIgnoreCase))
                {
                    ProjectPath = Path.GetDirectoryName(ProjectPath);
                }

                if (!Directory.Exists(ProjectPath))
                {
                    Console.WriteLine(" ");
                    Console.WriteLine($"Error: Could not find directory {ProjectPath}");

                    return 2;
                }
            }
            else
            {
                ProjectPath = Directory.GetCurrentDirectory();
            }

            if (!ValidateInputs())
            {
                return 1;
            }

            return ExecuteCore();
        }

        private bool ValidateInputs()
        {
            if (!FrameworkOption.HasValue())
            {
                Console.Error.WriteLine($"{FrameworkOption.LongName} not specified.");
                return false;
            }

            if (!ConfigurationOption.HasValue())
            {
                Console.Error.WriteLine($"{ConfigurationOption.LongName} not specified.");
                return false;
            }

            if (string.IsNullOrEmpty(ProjectPath))
            {
                Console.Error.WriteLine($"{ProjectArgument.Name} not specified.");
            }

            return true;
        }

        protected abstract int ExecuteCore();
    }
}
