using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Razor.Internal;
using Microsoft.Extensions.PlatformAbstractions;

namespace Microsoft.AspNetCore.Mvc.Razor.Precompilation
{
    public class PrecompilationCompilerCacheProvider : ICompilerCacheProvider
    {
        /// <summary>
        /// Initializes a new instance of <see cref="DefaultCompilerCacheProvider"/>.
        /// </summary>
        /// <param name="fileProviderAccessor">The <see cref="IRazorViewEngineFileProviderAccessor"/>.</param>
        public PrecompilationCompilerCacheProvider(IRazorViewEngineFileProviderAccessor fileProviderAccessor)
        {
            Cache = new CompilerCache(fileProviderAccessor.FileProvider);
            var precompiledViews = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (var assemblyPath in Directory.EnumerateFiles(PlatformServices.Default.Application.ApplicationBasePath, "*.dll"))
            {
                var assembly = LoadStream(assemblyPath);
                if (assembly != null)
                {
                    foreach (var type in assembly.ExportedTypes)
                    {
                        var typeInfo = type.GetTypeInfo();
                        var attribute = typeInfo.GetCustomAttribute<PagePathAttribute>(inherit: false);
                        if (attribute != null)
                        {
                            precompiledViews[attribute.Path] = type;
                        }
                    }
                }
            }

            Cache = new CompilerCache(fileProviderAccessor.FileProvider, precompiledViews);
        }

        /// <inheritdoc />
        public ICompilerCache Cache { get; }

        private Assembly LoadStream(string path)
        {
            try
            {
#if NET451
                return Assembly.LoadFrom(path);
#else
            return System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
#endif
            }
            catch
            {
                return null;
            }
        }

    }
}
