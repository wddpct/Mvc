using System;

namespace Microsoft.AspNetCore.Mvc.Razor
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class PagePathAttribute : Attribute
    {
        public PagePathAttribute(string path)
        {
            Path = path;
        }

        public string Path { get; }
    }
}
