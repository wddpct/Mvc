using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;
using Microsoft.AspNetCore.Mvc.Razor.Internal;
using Microsoft.AspNetCore.Razor.CodeGenerators;
using Microsoft.Cci;
using Microsoft.Cci.MutableCodeModel;
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
            var services = ConfigureDefaultServices();
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
            var compileResults = CompileViews(result);

            var host = new PeReader.DefaultHost();

            var assemblyPath = Path.Combine(outputPath, Path.GetFileName(ProjectPath) + ".dll");
            var module = host.LoadUnitFrom(assemblyPath) as IAssembly;
            module = new MetadataDeepCopier(host).Copy(module);
            var metadataRewriter = new AddResourcesRewriter(host, module, compileResults);

            var updatedModule = metadataRewriter.Rewrite(module);
            using (var stream = File.OpenWrite(Path.ChangeExtension(assemblyPath, ".new.dll")))
            {
                PeWriter.WritePeToStream(updatedModule, host, stream);
            }

            return result;
        }

        private class AddResourcesRewriter : MetadataRewriter
        {
            private readonly IAssembly _assembly;
            private readonly List<CompileOutputs> _outputs;

            public AddResourcesRewriter(IMetadataHost host, IAssembly assembly, List<CompileOutputs> outputs)
                : base(host)
            {
                _assembly = assembly;
                _outputs = outputs;
            }

            public override List<IResourceReference> Rewrite(List<IResourceReference> resourceReferences)
            {
                if (resourceReferences == null)
                {
                    resourceReferences = new List<IResourceReference>();
                }

                foreach (var output in _outputs)
                {
                    resourceReferences.Add(new ResourceReference
                    {
                        Name = host.NameTable.GetNameFor($"__RazorPrecompiledView__.{output.RelativePath}"),
                        DefiningAssembly = _assembly,
                        IsPublic = true,
                        Resource = new Resource
                        {
                            Data = output.AssemblyStream.ToArray().ToList(),
                            IsPublic = true,
                            DefiningAssembly = _assembly,
                        }
                    });
                }

                return resourceReferences;
            }
        }

        private List<CompileOutputs> CompileViews(PrecompilationResult result)
        {
            var compileOutputs = new List<CompileOutputs>();
            foreach (var cshtmlFilePath in Directory.EnumerateFiles(ProjectPath, "*.cshtml", SearchOption.AllDirectories))
            {
                var compileOutput = CompileView(result, cshtmlFilePath);
                compileOutputs.Add(compileOutput);
            }

            return compileOutputs;
        }

        private CompileOutputs CompileView(PrecompilationResult result, string cshtmlFilePath)
        {
            GeneratorResults generatorResults;
            var rootRelativePath = cshtmlFilePath.Substring(ProjectPath.Length + 1);
            using (var fileStream = File.OpenRead(cshtmlFilePath))
            {
                generatorResults = Host.GenerateCode(rootRelativePath, fileStream);
            }

            if (!generatorResults.Success)
            {
                result.RazorErrors.AddRange(generatorResults.ParserErrors);
                return null;
            }

            var compileOutputs = new CompileOutputs(rootRelativePath);
            var emitResult = CompilationService.EmitAssembly(
                generatorResults.GeneratedCode,
                compileOutputs.AssemblyStream,
                compileOutputs.PdbStream);

            if (!emitResult.Success)
            {
                result.RoslynErrors.AddRange(emitResult.Diagnostics);
                compileOutputs.Dispose();
                return null;
            }

            return compileOutputs;
        }

        private IServiceProvider ConfigureDefaultServices()
        {
            var services = new ServiceCollection();

            var applicationEnvironment = PlatformServices.Default.Application;
            services.AddSingleton(PlatformServices.Default.Application);
            var applicationName = Path.GetFileName(ProjectPath.TrimEnd('/'));
            services.AddSingleton<IHostingEnvironment>(new HostingEnvironment
            {
                ApplicationName = applicationName,
                WebRootFileProvider = new PhysicalFileProvider(ProjectPath)
            });
            services.Configure<RazorViewEngineOptions>(options =>
            {
                options.FileProviders.Clear();
                options.FileProviders.Add(new PhysicalFileProvider(ProjectPath));
            });
            var diagnosticSource = new DiagnosticListener("Microsoft.AspNetCore");
            services.AddSingleton<DiagnosticSource>(diagnosticSource);
            services.AddLogging();
            var mvcBuilder = services.AddMvc();

            services.AddSingleton<ObjectPoolProvider>(new DefaultObjectPoolProvider());

            ConfigureCompilation(mvcBuilder);

            return services.BuildServiceProvider();
        }

        private void ConfigureCompilation(IMvcBuilder mvcBuilder)
        {
            if (!ConfigureCompilationType.HasValue())
            {
                return;
            }

            var type = Type.GetType(ConfigureCompilationType.Value());
            var configureMethod = type.GetMethod("ConfigureMvc", BindingFlags.Public | BindingFlags.Static);
            if (configureMethod == null)
            {
                throw new InvalidOperationException("Could not find Configure");
            }

            configureMethod.Invoke(obj: null, parameters: new[] { mvcBuilder });
        }

        private class CompileOutputs : IDisposable
        {
            public CompileOutputs(string relativePath)
            {
                RelativePath = relativePath;
            }

            public MemoryStream AssemblyStream { get; } = new MemoryStream();

            public MemoryStream PdbStream { get; } = new MemoryStream();

            public string RelativePath { get; }

            public void Dispose()
            {
                AssemblyStream.Dispose();
                PdbStream.Dispose();
            }
        }
    }
}
