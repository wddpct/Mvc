// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace MvcSandbox.Controllers
{
    public class HomeController : Controller
    {
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly ILogger<HomeController> _logger;
        private readonly FormOptions _defaultFormOptions;

        public HomeController(IHostingEnvironment hostingEnvironment, ILogger<HomeController> logger)
        {
            _hostingEnvironment = hostingEnvironment;
            _logger = logger;
            _defaultFormOptions = new FormOptions();
        }

        public IActionResult Index()
        {
            return View();
        }

        // For requests where its a mix of form url encoded data and files
        [DisableFormValueModelBinding]
        public async Task<IActionResult> Upload()
        {
            var requestContentType = HttpContext.Request.ContentType;
            if (!IsMultipartContentType(requestContentType))
            {
                return BadRequest($"Expected a multipart request, but got '{requestContentType}'.");
            }

            //TODO: HOW CAN I MODEL BIND ALL THIS FORM URL ENCODED DATA LATER
            // Used to accumulate all the form url encoded key value pairs in the request.
            var formAccumulator = new KeyValueAccumulator();

            var boundary = GetBoundary(MediaTypeHeaderValue.Parse(requestContentType));
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var section = await reader.ReadNextSectionAsync();
            while (section != null)
            {
                ContentDispositionHeaderValue contentDisposition;
                ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out contentDisposition);

                if (HasFileContentDisposition(contentDisposition))
                {
                    var name = HeaderUtilities.RemoveQuotes(contentDisposition.Name) ?? string.Empty;
                    var fileName = HeaderUtilities.RemoveQuotes(contentDisposition.FileName) ?? string.Empty;

                    // Copy the uploaded file to a local disk.
                    var targetFilePath = Path.Combine(_hostingEnvironment.ContentRootPath, Guid.NewGuid().ToString());
                    using (var targetStream = System.IO.File.Create(targetFilePath))
                    {
                        await section.Body.CopyToAsync(targetStream);

                        _logger.LogInformation($"Copied the uploaded file '{fileName}' to '{targetFilePath}'.");
                    }
                }
                else if (HasFormDataContentDisposition(contentDisposition))
                {
                    // Content-Disposition: form-data; name="key"
                    //
                    // value

                    // Do not limit the key name length here because the mulipart headers length
                    // limit is already in effect.
                    var key = HeaderUtilities.RemoveQuotes(contentDisposition.Name);
                    MediaTypeHeaderValue mediaType;
                    MediaTypeHeaderValue.TryParse(section.ContentType, out mediaType);
                    var encoding = FilterEncoding(mediaType?.Encoding);
                    using (var streamReader = new StreamReader(
                        section.Body,
                        encoding,
                        detectEncodingFromByteOrderMarks: true,
                        bufferSize: 1024,
                        leaveOpen: true))
                    {
                        // The value length limit is enforced by MultipartBodyLengthLimit
                        var value = await streamReader.ReadToEndAsync();
                        formAccumulator.Append(key, value);

                        if (formAccumulator.Count > _defaultFormOptions.KeyCountLimit)
                        {
                            throw new InvalidDataException(
                                $"Form key count limit {_defaultFormOptions.KeyCountLimit} exceeded.");
                        }
                    }
                }

                // Drains any remaining section body that has not been consumed and
                // reads the headers for the next section.
                section = await reader.ReadNextSectionAsync();
            }

            return StatusCode((int)HttpStatusCode.Accepted);
        }

        //TODO: move all these methods to a helper class

        // Content-Type: multipart/form-data; boundary="----WebKitFormBoundarymx2fSWqWSd0OxQqq"
        // The spec says 70 characters is a reasonable limit.
        private string GetBoundary(MediaTypeHeaderValue contentType)
        {
            var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary);
            if (string.IsNullOrWhiteSpace(boundary))
            {
                throw new InvalidDataException("Missing content-type boundary.");
            }

            if (boundary.Length > _defaultFormOptions.MultipartBoundaryLengthLimit)
            {
                throw new InvalidDataException(
                    $"Multipart boundary length limit {_defaultFormOptions.MultipartBoundaryLengthLimit} exceeded.");
            }

            return boundary;
        }

        private static Encoding FilterEncoding(Encoding encoding)
        {
            // UTF-7 is insecure and should not be honored. UTF-8 will succeed for most cases.
            if (encoding == null || Encoding.UTF7.Equals(encoding))
            {
                return Encoding.UTF8;
            }
            return encoding;
        }

        private static bool IsMultipartContentType(string contentType)
        {
            return !string.IsNullOrEmpty(contentType)
                && contentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool HasFormDataContentDisposition(ContentDispositionHeaderValue contentDisposition)
        {
            // Content-Disposition: form-data; name="key";
            return contentDisposition != null
                && contentDisposition.DispositionType.Equals("form-data")
                && string.IsNullOrEmpty(contentDisposition.FileName)
                && string.IsNullOrEmpty(contentDisposition.FileNameStar);
        }

        private bool HasFileContentDisposition(ContentDispositionHeaderValue contentDisposition)
        {
            // Content-Disposition: form-data; name="myfile1"; filename="Misc 002.jpg"
            return contentDisposition != null
                && contentDisposition.DispositionType.Equals("form-data")
                && (!string.IsNullOrEmpty(contentDisposition.FileName)
                || !string.IsNullOrEmpty(contentDisposition.FileNameStar));
        }
    }

    public class CustomerInfo
    {
        public string Name { get; set; }

        public int Age { get; set; }

        public int Zipcode { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            //var formValueProviderFactory = context.ValueProviderFactories
            //        .OfType<FormValueProviderFactory>()
            //        .FirstOrDefault();
            //if (formValueProviderFactory != null)
            //{
            //    context.ValueProviderFactories.Remove(formValueProviderFactory);
            //}

            //var jqueryFormValueProviderFactory = context.ValueProviderFactories
            //    .OfType<JQueryFormValueProviderFactory>()
            //    .FirstOrDefault();
            //if (jqueryFormValueProviderFactory != null)
            //{
            //    context.ValueProviderFactories.Remove(jqueryFormValueProviderFactory);
            //}
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
}
