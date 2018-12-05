using BaGet.Decompiler.Extensions;
using Mono.Cecil;

namespace BaGet.Decompiler.Decompilation
{
    internal class Filter
    {
        public bool Include(MethodDefinition method)
        {
            if (method.IsSpecialName)
                return false;

            // Skip finalizers
            if (method.IsFinalizer())
                return false;

            // Publics
            if (method.IsPublic)
                return true;

            // Protected, in non-sealed types
            if (method.IsFamily && !method.DeclaringType.IsSealed)
                return true;

            return false;
        }

        public bool Include(PropertyDefinition property)
        {
            if (property.IsSpecialName)
                return false;

            // Publics
            if (property.IsPublic())
                return true;

            // Protected, in non-sealed types
            if (property.IsFamily() && !property.DeclaringType.IsSealed)
                return true;

            return !property.IsSpecialName;
        }

        public bool Include(TypeDefinition type)
        {
            return (type.IsPublic || type.IsNestedFamily) && !type.IsSpecialName;
        }

        public bool Include(FieldDefinition field)
        {
            return (field.IsPublic || field.IsFamily) && !field.IsSpecialName;
        }

        public bool Include(EventDefinition @event)
        {
            return (@event.IsPublic() || @event.IsFamily()) && !@event.IsSpecialName;
        }
    }
}