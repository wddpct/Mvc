// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Microsoft.AspNetCore.Mvc.TagHelpers
{
    /// <summary>
    /// <see cref="ITagHelper"/> implementation targeting all form elements
    /// to generate content before the form end tag.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [HtmlTargetElement("form")]
    public class RenderAtEndOfFormTagHelper : TagHelper
    {
        /// <inheritdoc />
        public override int Order => -999;

        [HtmlAttributeNotBound]
        [ViewContext]
        public ViewContext ViewContext { get; set; }

        /// <inheritdoc />
        public override void Init(TagHelperContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Console.WriteLine(">>>>>>>>>>>>>>>>Inside RenderAtEndOfFormTagHelper.Init. New FormContext created");
            // Push the new FormContext.
            ViewContext.FormContext = new FormContext
            {
                CanRenderAtEndOfForm = true
            };
        }

        /// <inheritdoc />
        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            await output.GetChildContentAsync();

            var formContext = ViewContext.FormContext;
            Console.WriteLine($">>>>>>>>>>>>>>>>Inside RenderAtEndOfFormTagHelper.ProcessAsync: ViewContext.FormContext.HasEndOfFormContent value is {ViewContext.FormContext.HasEndOfFormContent}");
            if (formContext.HasEndOfFormContent)
            {
                // Perf: Avoid allocating enumerator
                for (var i = 0; i < formContext.EndOfFormContent.Count; i++)
                {
                    var sw = new StringWriter();
                    formContext.EndOfFormContent[i].WriteTo(sw, NullHtmlEncoder.Default);
                    Console.WriteLine(">>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>RenderAtEndFormTagHelper's end of form content:" + sw.ToString());
                    sw.Dispose();
                    output.PostContent.AppendHtml(formContext.EndOfFormContent[i]);
                }
            }

            Console.WriteLine(">>>>>>>>>>>>>>>>Inside RenderAtEndOfFormTagHelper.ProcessAsync. Reset the formcontext by creating a new one.");
            // Reset the FormContext
            ViewContext.FormContext = new FormContext();
        }
    }
}
