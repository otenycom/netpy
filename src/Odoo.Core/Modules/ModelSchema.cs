using System;
using System.Collections.Generic;

namespace Odoo.Core.Modules
{
    public class ModelSchema
    {
        public string ModelName { get; }
        public List<Type> ContributingInterfaces { get; }
        public Dictionary<string, FieldSchema> Fields { get; }
        public ModelHandle Token { get; }

        public ModelSchema(string modelName, ModelHandle token)
        {
            ModelName = modelName;
            Token = token;
            ContributingInterfaces = new List<Type>();
            Fields = new Dictionary<string, FieldSchema>();
        }
    }
}