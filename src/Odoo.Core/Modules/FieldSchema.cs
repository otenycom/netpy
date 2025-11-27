using System;

namespace Odoo.Core.Modules
{
    public class FieldSchema
    {
        public string FieldName { get; }
        public Type FieldType { get; }
        public bool IsReadOnly { get; }
        public Type ContributingInterface { get; }
        public FieldHandle Token { get; }

        public FieldSchema(string fieldName, Type fieldType, bool isReadOnly, Type contributingInterface, FieldHandle token)
        {
            FieldName = fieldName;
            FieldType = fieldType;
            IsReadOnly = isReadOnly;
            ContributingInterface = contributingInterface;
            Token = token;
        }
    }
}