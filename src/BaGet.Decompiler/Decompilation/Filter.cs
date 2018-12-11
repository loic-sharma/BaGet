using ICSharpCode.Decompiler.TypeSystem;

namespace BaGet.Decompiler.Decompilation
{
    internal class Filter
    {
        public bool Include(IMethod method)
        {
            // Skip finalizers
            if (method.IsDestructor)
                return false;

            // Publics
            if (method.Accessibility == Accessibility.Public)
                return true;

            // Protected, in non-sealed types
            if (method.Accessibility == Accessibility.Protected && !method.DeclaringType.GetDefinition().IsSealed)
                return true;

            return false;
        }

        public bool Include(IProperty property)
        {
            // Publics
            if (property.Accessibility == Accessibility.Public)
                return true;

            // Protected, in non-sealed types
            if (property.Accessibility == Accessibility.Protected && !property.DeclaringType.GetDefinition().IsSealed)
                return true;

            return false;
        }

        public bool Include(ITypeDefinition type)
        {
            return type.Accessibility == Accessibility.Public || type.Accessibility == Accessibility.Protected;
        }

        public bool Include(IField field)
        {
            return field.Accessibility == Accessibility.Public || field.Accessibility == Accessibility.Protected;
        }

        public bool Include(IEvent @event)
        {
            return @event.Accessibility == Accessibility.Public || @event.Accessibility == Accessibility.Protected;
        }
    }
}