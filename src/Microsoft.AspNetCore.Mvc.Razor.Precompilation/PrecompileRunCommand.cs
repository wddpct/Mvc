using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;
using Microsoft.AspNetCore.Mvc.Razor.Internal;
using Microsoft.AspNetCore.Razor.CodeGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.DotNet.InternalAbstractions;
using Microsoft.DotNet.ProjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.PlatformAbstractions;
using NuGet.Frameworks;

namespace Microsoft.AspNetCore.Mvc.Razor.Precompilation
{
    public class PrecompileRunCommand : PrecompileCommandBase
    {
        private IMvcRazorHost Host { get; set; }

        private DefaultRoslynCompilationService CompilationService { get; set; }

        protected override int ExecuteCore()
        {
            var services = ConfigureDefaultServices(ProjectPath);
            Host = services.GetRequiredService<IMvcRazorHost>();
            CompilationService = services.GetRequiredService<ICompilationService>() as DefaultRoslynCompilationService;

            var workspace = new BuildWorkspace(ProjectReaderSettings.ReadFromEnvironment());
            var projectContext = workspace.GetProjectContext(
                ProjectPath,
                NuGetFramework.Parse(FrameworkOption.Value()));
            var runtimeContext = workspace.GetRuntimeContext(
                projectContext,
                RuntimeEnvironmentRidExtensions.GetAllCandidateRuntimeIdentifiers());

            var outputs = runtimeContext.GetOutputPaths(ConfigurationOption.Value());
            string outputPath;
            if (!string.IsNullOrEmpty(runtimeContext.RuntimeIdentifier))
            {
                outputPath = outputs.RuntimeOutputPath;
            }
            else
            {
                outputPath = outputs.CompilationOutputPath;
            }

            var result = GenerateViews(outputPath);
            if (result.HasErrors)
            {
                foreach (var error in result.RazorErrors)
                {
                    Console.Error.WriteLine($"{error.Location.FilePath} ({error.Location.LineIndex}): {error.Message}");
                }

                foreach (var error in result.RoslynErrors)
                {
                    Console.Error.WriteLine(CSharpDiagnosticFormatter.Instance.Format(error));
                }

                return 1;
            }

            return 0;
        }

        private PrecompilationResult GenerateViews(string outputPath)
        {
            var result = new PrecompilationResult();
            var compilationContext = CompilationService.CreateCompilationContext("Generated.Views.dll");
            var fileMappings = new Dictionary<string, Type>();

            foreach (var cshtmlFilePath in Directory.EnumerateFiles(ProjectPath, "*.cshtml", SearchOption.AllDirectories))
            {
                var generatorResults = GenerateCode(cshtmlFilePath);

                if (!generatorResults.Success)
                {
                    result.RazorErrors.AddRange(generatorResults.ParserErrors);
                    continue;
                }

                CompilationService.AddToCompilation(
                    compilationContext,
                    cshtmlFilePath,
                    generatorResults.GeneratedCode);
            }

            var emitResult = compilationContext.Compilation.Emit(
                outputPath: Path.Combine(outputPath, "Generated.Views.dll"),
                pdbPath: Path.Combine(outputPath, "Generated.Views.pdb"));

            if (!emitResult.Success)
            {
                result.RoslynErrors.AddRange(emitResult.Diagnostics);
            }

            return result;
        }

        private GeneratorResults GenerateCode(string cshtmlFile)
        {
            using (var fileStream = File.OpenRead(cshtmlFile))
            {
                var rootRelativePath = cshtmlFile.Substring(ProjectPath.Length + 1);
                return Host.GenerateCode(rootRelativePath, fileStream);
            }
        }

        private static IServiceProvider ConfigureDefaultServices(string projectPath)
        {
            var services = new ServiceCollection();

            var applicationEnvironment = PlatformServices.Default.Application;
            services.AddSingleton(PlatformServices.Default.Application);
            services.AddSingleton<IHostingEnvironment>(new HostingEnvironment
            {
                ApplicationName = Path.GetFileName(projectPath.TrimEnd('/')),
                WebRootFileProvider = new PhysicalFileProvider(projectPath)
            });
            services.Configure<RazorViewEngineOptions>(options =>
            {
                options.FileProviders.Clear();
                options.FileProviders.Add(new PhysicalFileProvider(projectPath));
            });
            var diagnosticSource = new DiagnosticListener("Microsoft.AspNetCore");
            services.AddSingleton<DiagnosticSource>(diagnosticSource);
            services.AddLogging();
            services.AddMvc();

            services.AddSingleton<ObjectPoolProvider>(new DefaultObjectPoolProvider());

            return services.BuildServiceProvider();
        }
    }
}
