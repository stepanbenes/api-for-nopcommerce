﻿namespace Nop.Plugin.Api.Attributes
{
    public abstract class BaseValidationAttribute : Attribute
    {
        public abstract Task ValidateAsync(object instance);
        public abstract Dictionary<string, string> GetErrors();
    }
}
