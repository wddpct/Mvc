using System.Collections.Generic;
using Microsoft.AspNetCore.Razor;
using Microsoft.CodeAnalysis;

namespace Microsoft.AspNetCore.Mvc.Razor.Precompilation
{
    public class PrecompilationResult
    {
        public string OutputPath { get; set; }

        public List<RazorError> RazorErrors { get; } = new List<RazorError>();

        public List<Diagnostic> RoslynErrors { get; } = new List<Diagnostic>();

        public bool HasErrors => RazorErrors.Count + RoslynErrors.Count > 0;
    }
}
