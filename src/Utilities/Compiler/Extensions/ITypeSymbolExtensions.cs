﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Analyzer.Utilities.Extensions
{
    internal static class ITypeSymbolExtensions
    {
        public static bool IsPrimitiveType(this ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_Char:
                case SpecialType.System_Double:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                case SpecialType.System_SByte:
                case SpecialType.System_Single:
                case SpecialType.System_String:
                    return true;
                default:
                    return false;
            }
        }

        public static bool Inherits([NotNullWhen(returnValue: true)] this ITypeSymbol? type, [NotNullWhen(returnValue: true)]  ITypeSymbol? possibleBase)
        {
            if (type == null || possibleBase == null)
            {
                return false;
            }

            switch (possibleBase.TypeKind)
            {
                case TypeKind.Class:
                    if (type.TypeKind == TypeKind.Interface)
                    {
                        return false;
                    }

                    return DerivesFrom(type, possibleBase, baseTypesOnly: true);

                case TypeKind.Interface:
                    return DerivesFrom(type, possibleBase);

                default:
                    return false;
            }
        }

        public static IEnumerable<INamedTypeSymbol> GetBaseTypes(this ITypeSymbol type)
        {
            INamedTypeSymbol current = type.BaseType;
            while (current != null)
            {
                yield return current;
                current = current.BaseType;
            }
        }

        public static IEnumerable<ITypeSymbol> GetBaseTypesAndThis(this ITypeSymbol type)
        {
            ITypeSymbol current = type;
            while (current != null)
            {
                yield return current;
                current = current.BaseType;
            }
        }

        public static bool DerivesFrom([NotNullWhen(returnValue: true)] this ITypeSymbol? symbol, [NotNullWhen(returnValue: true)] ITypeSymbol? candidateBaseType, bool baseTypesOnly = false, bool checkTypeParameterConstraints = true)
        {
            if (candidateBaseType == null || symbol == null)
            {
                return false;
            }

            if (!baseTypesOnly)
            {
                var allInterfaces = symbol.AllInterfaces.OfType<ITypeSymbol>();
                if (candidateBaseType.OriginalDefinition.Equals(candidateBaseType))
                {
                    // Candidate base type is not a constructed generic type, so use original definition for interfaces.
                    allInterfaces = allInterfaces.Select(i => i.OriginalDefinition);
                }

                if (allInterfaces.Contains(candidateBaseType))
                {
                    return true;
                }
            }

            if (checkTypeParameterConstraints && symbol.TypeKind == TypeKind.TypeParameter)
            {
                var typeParameterSymbol = (ITypeParameterSymbol)symbol;
                foreach (var constraintType in typeParameterSymbol.ConstraintTypes)
                {
                    if (constraintType.DerivesFrom(candidateBaseType, baseTypesOnly, checkTypeParameterConstraints))
                    {
                        return true;
                    }
                }
            }

            while (symbol != null)
            {
                if (symbol.Equals(candidateBaseType))
                {
                    return true;
                }

                symbol = symbol.BaseType;
            }

            return false;
        }

        /// <summary>
        /// Indicates if the given <paramref name="type"/> is a reference type that implements <paramref name="iDisposable"/> or System.IAsyncDisposable or is <see cref="IDisposable"/> or System.IAsyncDisposable type itself.
        /// </summary>
        public static bool IsDisposable(this ITypeSymbol type,
            INamedTypeSymbol? iDisposable,
            INamedTypeSymbol? iAsyncDisposable)
        {
            return type.IsReferenceType &&
                (IsInterfaceOrImplementsInterface(type, iDisposable) ||
                 IsInterfaceOrImplementsInterface(type, iAsyncDisposable));

            static bool IsInterfaceOrImplementsInterface(ITypeSymbol type, INamedTypeSymbol? interfaceType)
                => interfaceType != null &&
                   (Equals(type, interfaceType) || type.AllInterfaces.Contains(interfaceType));
        }

        /// <summary>
        /// Gets all attributes directly applied to the type or inherited from a base type.
        /// </summary>
        /// <param name="type">The type symbol.</param>
        /// <param name="attributeUsageAttribute">The compilation symbol for <see cref="AttributeUsageAttribute"/>.</param>
        public static IEnumerable<AttributeData> GetApplicableAttributes(this INamedTypeSymbol type, INamedTypeSymbol? attributeUsageAttribute)
        {
            var attributes = new List<AttributeData>();
            var onlyIncludeInherited = false;

            while (type != null)
            {
                var current = type.GetAttributes();
                if (!onlyIncludeInherited || attributeUsageAttribute is null)
                {
                    attributes.AddRange(current);
                }
                else
                {
                    foreach (var attribute in current)
                    {
                        if (!IsInheritedAttribute(attribute, attributeUsageAttribute))
                        {
                            continue;
                        }

                        attributes.Add(attribute);
                    }
                }

                type = type.BaseType;
                onlyIncludeInherited = true;
            }

            return attributes;

            // Local functions
            static bool IsInheritedAttribute(AttributeData attributeData, INamedTypeSymbol attributeUsageAttribute)
            {
                for (var currentAttributeClass = attributeData.AttributeClass;
                    currentAttributeClass is object;
                    currentAttributeClass = currentAttributeClass.BaseType)
                {
                    foreach (var attributeClassData in currentAttributeClass.GetAttributes())
                    {
                        if (!Equals(attributeClassData.AttributeClass, attributeUsageAttribute))
                        {
                            continue;
                        }

                        foreach (var (name, typedConstant) in attributeClassData.NamedArguments)
                        {
                            if (name != nameof(AttributeUsageAttribute.Inherited))
                            {
                                continue;
                            }

                            // The default is true, so use that when explicitly specified and for cases where the value
                            // is not a boolean (i.e. compilation error scenarios).
                            return !Equals(false, typedConstant.Value);
                        }

                        // [AttributeUsage] was found, but did not specify Inherited explicitly. The default is true.
                        return true;
                    }
                }

                // [AttributeUsage] was not found. The default is true.
                return true;
            }
        }

        public static IEnumerable<AttributeData> GetApplicableExportAttributes(this INamedTypeSymbol? type, INamedTypeSymbol? exportAttributeV1, INamedTypeSymbol? exportAttributeV2, INamedTypeSymbol? inheritedExportAttribute)
        {
            var attributes = new List<AttributeData>();
            var onlyIncludeInherited = false;

            while (type != null)
            {
                var current = type.GetAttributes();
                foreach (var attribute in current)
                {
                    if (attribute.AttributeClass.Inherits(inheritedExportAttribute))
                    {
                        attributes.Add(attribute);
                    }
                    else if (!onlyIncludeInherited)
                    {
                        if (attribute.AttributeClass.Inherits(exportAttributeV1)
                            || attribute.AttributeClass.Inherits(exportAttributeV2))
                        {
                            attributes.Add(attribute);
                        }
                    }
                }

                if (inheritedExportAttribute is null)
                {
                    break;
                }

                type = type.BaseType;
                onlyIncludeInherited = true;
            }

            return attributes;
        }

        public static bool IsAttribute(this ITypeSymbol symbol)
        {
            for (INamedTypeSymbol b = symbol.BaseType; b != null; b = b.BaseType)
            {
                if (b.MetadataName == "Attribute" &&
                     b.ContainingType == null &&
                     b.ContainingNamespace != null &&
                     b.ContainingNamespace.Name == "System" &&
                     b.ContainingNamespace.ContainingNamespace != null &&
                     b.ContainingNamespace.ContainingNamespace.IsGlobalNamespace)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasValueCopySemantics(this ITypeSymbol typeSymbol)
            => typeSymbol.IsValueType || typeSymbol.SpecialType == SpecialType.System_String;

        public static bool IsNonNullableValueType([NotNullWhen(returnValue: true)] this ITypeSymbol? typeSymbol)
            => typeSymbol != null && typeSymbol.IsValueType && typeSymbol.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;

        public static bool IsNullableValueType([NotNullWhen(returnValue: true)] this ITypeSymbol? typeSymbol)
            => typeSymbol != null && typeSymbol.IsValueType && typeSymbol.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

        public static bool IsReferenceTypeOrNullableValueType([NotNullWhen(returnValue: true)] this ITypeSymbol? typeSymbol)
            => typeSymbol != null && (typeSymbol.IsReferenceType || typeSymbol.IsNullableValueType());

        public static bool IsNullableOfBoolean([NotNullWhen(returnValue: true)] this ITypeSymbol? typeSymbol)
            => typeSymbol.IsNullableValueType() && ((INamedTypeSymbol)typeSymbol).TypeArguments[0].SpecialType == SpecialType.System_Boolean;

#if HAS_IOPERATION
        public static ITypeSymbol GetUnderlyingValueTupleTypeOrThis(this ITypeSymbol typeSymbol)
            => (typeSymbol as INamedTypeSymbol)?.TupleUnderlyingType ?? typeSymbol;
#endif

        /// <summary>
        /// Checks whether the current type contains one of the following count property:
        ///     - <see cref="System.Collections.ICollection.Count"/>
        ///     - <see cref="System.Collections.Generic.ICollection{T}.Count"/>
        ///     - <see cref="System.Collections.Generic.IReadOnlyCollection{T}.Count"/>
        /// </summary>
        /// <param name="invocationTarget">The type to check</param>
        /// <param name="wellKnownTypeProvider">An instance of the <see cref="WellKnownTypeProvider"/> used to access the three described known types.</param>
        /// <returns><c>true</c> when the type contains one of the supported collection count property; otherwise <c>false</c>.</returns>
        public static bool HasAnyCollectionCountProperty([NotNullWhen(returnValue: true)] this ITypeSymbol? invocationTarget, WellKnownTypeProvider wellKnownTypeProvider)
        {
            const string countPropertyName = "Count";

            if (invocationTarget == null
                || !wellKnownTypeProvider.TryGetOrCreateTypeByMetadataName(WellKnownTypeNames.SystemCollectionsICollection, out var iCollection)
                || !wellKnownTypeProvider.TryGetOrCreateTypeByMetadataName(WellKnownTypeNames.SystemCollectionsGenericICollection1, out var iCollectionOfT)
                || !wellKnownTypeProvider.TryGetOrCreateTypeByMetadataName(WellKnownTypeNames.SystemCollectionsGenericIReadOnlyCollection1, out var iReadOnlyCollectionOfT))
            {
                return false;
            }

            if (isAnySupportedCollectionType(invocationTarget))
            {
                return true;
            }

            if (invocationTarget.TypeKind == TypeKind.Interface)
            {
                if (invocationTarget.GetMembers(countPropertyName).OfType<IPropertySymbol>().Any())
                {
                    return false;
                }

                foreach (var @interface in invocationTarget.AllInterfaces)
                {
                    if (isAnySupportedCollectionType(@interface))
                    {
                        return true;
                    }
                }
            }
            else
            {
                foreach (var @interface in invocationTarget.AllInterfaces)
                {
                    if (isAnySupportedCollectionType(@interface)
                        && invocationTarget.FindImplementationForInterfaceMember(@interface.GetMembers(countPropertyName)[0]) is IPropertySymbol propertyImplementation
                        && !propertyImplementation.ExplicitInterfaceImplementations.Any())
                    {
                        return true;
                    }
                }
            }

            return false;

            bool isAnySupportedCollectionType(ITypeSymbol type)
            {
                RoslynDebug.Assert(iCollection != null);
                RoslynDebug.Assert(iCollectionOfT != null);
                RoslynDebug.Assert(iReadOnlyCollectionOfT != null);

                return type.OriginalDefinition is INamedTypeSymbol originalDefinition &&
                    (iCollection.Equals(originalDefinition) ||
                     iCollectionOfT.Equals(originalDefinition) ||
                     iReadOnlyCollectionOfT.Equals(originalDefinition));
            }
        }
    }
}
