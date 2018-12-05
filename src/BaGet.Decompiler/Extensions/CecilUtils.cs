using System;
using System.Linq;
using Mono.Cecil;

namespace BaGet.Decompiler.Extensions
{
    internal static class CecilUtils
    {
        public static bool IsFinalizer(this MethodDefinition method)
        {
            return method.Overrides.Any(s => s.DeclaringType.FullName == "System.Object" && s.Name == "Finalize");
        }

        public static bool IsPublic(this PropertyDefinition property)
        {
            return property.CheckMethodsAny(s => s.IsPublic);
        }

        public static bool IsFamily(this PropertyDefinition property)
        {
            return property.CheckMethodsAny(s => s.IsFamily);
        }

        private static bool CheckMethodsAny(this PropertyDefinition property, Func<MethodDefinition, bool> func)
        {
            return property.GetMethod != null && func(property.GetMethod) ||
                   property.SetMethod != null && func(property.SetMethod);
        }

        public static bool IsPublic(this EventDefinition @event)
        {
            return @event.CheckMethodsAny(s => s.IsPublic);
        }

        public static bool IsFamily(this EventDefinition @event)
        {
            return @event.CheckMethodsAny(s => s.IsFamily);
        }

        private static bool CheckMethodsAny(this EventDefinition @event, Func<MethodDefinition, bool> func)
        {
            return @event.AddMethod != null && func(@event.AddMethod) ||
                   @event.RemoveMethod != null && func(@event.RemoveMethod);
        }
    }
}