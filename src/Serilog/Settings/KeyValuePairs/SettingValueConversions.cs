﻿// Copyright 2013-2015 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Serilog.Settings.KeyValuePairs
{
    class SettingValueConversions
    {
        // should match "The.NameSpace.TypeName::MemberName" optionnally followed by
        // usual assembly qualifiers like : 
        // ", MyAssembly, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"
        static Regex StaticMemberAccessorRegex = new Regex("^(?<shortTypeName>[^:]+)::(?<memberName>[A-Za-z][A-Za-z0-9]*)(?<typeNameExtraQualifiers>[^:]*)$");

        static Dictionary<Type, Func<string, object>> ExtendedTypeConversions = new Dictionary<Type, Func<string, object>>
            {
                { typeof(Uri), s => new Uri(s) },
                { typeof(TimeSpan), s => TimeSpan.Parse(s) }
            };

        public static object ConvertToType(string value, Type toType)
        {
            if (TryParseStaticMemberAccessor(value, out var accessorTypeName, out var memberName))
            {
                var accessorType = Type.GetType(accessorTypeName, throwOnError: true);
                var publicStaticPropertyInfo = accessorType.GetTypeInfo().DeclaredProperties
                    .Where(x => x.Name == memberName)
                    .Where(x => x.GetMethod != null)
                    .Where(x => x.GetMethod.IsPublic)
                    .FirstOrDefault(x => x.GetMethod.IsStatic);

                if (publicStaticPropertyInfo == null)
                {
                    throw new InvalidOperationException($"Could not find public static property `{memberName}` on type `{accessorTypeName}`");
                }
                return publicStaticPropertyInfo.GetValue(null); // static property, no instance to pass
            }

            var toTypeInfo = toType.GetTypeInfo();
            if (toTypeInfo.IsGenericType && toType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                if (string.IsNullOrEmpty(value))
                    return null;

                // unwrap Nullable<> type since we're not handling null situations
                toType = toTypeInfo.GenericTypeArguments[0];
                toTypeInfo = toType.GetTypeInfo();
            }

            if (toTypeInfo.IsEnum)
                return Enum.Parse(toType, value);

            var convertor = ExtendedTypeConversions
                .Where(t => t.Key.GetTypeInfo().IsAssignableFrom(toTypeInfo))
                .Select(t => t.Value)
                .FirstOrDefault();

            if (convertor != null)
                return convertor(value);

            if ((toTypeInfo.IsInterface || toTypeInfo.IsAbstract) && !string.IsNullOrWhiteSpace(value))
            {
                var type = Type.GetType(value.Trim(), throwOnError: false);
                if (type != null)
                {
                    var ctor = type.GetTypeInfo().DeclaredConstructors.FirstOrDefault(ci =>
                    {
                        var parameters = ci.GetParameters();
                        return parameters.Length == 0 || parameters.All(pi => pi.HasDefaultValue);
                    });

                    if (ctor == null)
                        throw new InvalidOperationException($"A default constructor was not found on {type.FullName}.");

                    var call = ctor.GetParameters().Select(pi => pi.DefaultValue).ToArray();
                    return ctor.Invoke(call);
                }
            }

            return Convert.ChangeType(value, toType);
        }

        internal static bool TryParseStaticMemberAccessor(string input, out string accessorTypeName, out string memberName)
        {
            if (input == null)
            {
                accessorTypeName = null;
                memberName = null;
                return false;
            }
            if (StaticMemberAccessorRegex.IsMatch(input))
            {
                var match = StaticMemberAccessorRegex.Match(input);
                var shortAccessorTypeName = match.Groups["shortTypeName"].Value;
                var rawMemberName = match.Groups["memberName"].Value;
                var extraQualifiers = match.Groups["typeNameExtraQualifiers"].Value;

                memberName = rawMemberName.Trim();
                accessorTypeName = shortAccessorTypeName.Trim() + extraQualifiers.TrimEnd();
                return true;
            }
            accessorTypeName = null;
            memberName = null;
            return false;
        }
    }
}
