using System;

namespace Odoo.Core.Pipeline
{
    /// <summary>
    /// Marks a method as business logic for a model.
    /// Source generator creates:
    /// 1. Typed Super delegate in Odoo.Generated.{Model}.Super namespace
    /// 2. Pipeline registration in IModuleRegistrar
    /// 3. Extension method on RecordSet for invocation
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class OdooLogicAttribute : Attribute
    {
        public string ModelName { get; }
        public string MethodName { get; }
        
        public OdooLogicAttribute(string modelName, string methodName)
        {
            ModelName = modelName;
            MethodName = methodName;
        }
    }
}