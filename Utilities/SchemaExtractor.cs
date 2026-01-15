using System;
using System.Reflection;
using System.Text;
using TwinAgentsLibrary.Models;
using System.Collections.Generic;

namespace TwinAgentsNetwork.Utilities
{
    /// <summary>
    /// Utility class to extract schema information from classes using .NET reflection
    /// </summary>
    public static class SchemaExtractor
    {
        /// <summary>
        /// Extracts the schema for FamilyData using the correct JSON property names that match Cosmos DB storage
        /// </summary>
        /// <returns>String representation of the FamilyData class schema with correct Cosmos DB field names</returns>
        public static string ExtractFamilyDataSchema()
        {
            try
            {
                // Hardcoded mapping based on the ToDict() method in FamilyData class
                // This matches the actual JSON property names used in Cosmos DB
                var cosmosDbFieldMapping = new Dictionary<string, string>
                {
                    ["id"] = "Id - Unique identifier for the family member record",
                    ["TwinID"] = "TwinID - Associated twin identifier (partition key)",
                    ["parentesco"] = "Parentesco - Relationship type (padre, madre, hermano, hermana, hijo, hija, etc.)",
                    ["nombre"] = "Nombre - First name of the family member",
                    ["apellido"] = "Apellido - Last name/surname of the family member",
                    ["email"] = "Email - Email address",
                    ["telefono"] = "Telefono - Phone number",
                    ["fecha_nacimiento"] = "FechaNacimiento - Date of birth",
                    ["nombre_twin"] = "NombreTwin - Twin's name reference",
                    ["direccion_completa"] = "DireccionCompleta - Complete address",
                    ["pais_nacimiento"] = "PaisNacimiento - Country of birth",
                    ["nacionalidad"] = "Nacionalidad - Nationality",
                    ["genero"] = "Genero - Gender",
                    ["ocupacion"] = "Ocupacion - Occupation/profession",
                    ["intereses"] = "Intereses - Interests and hobbies (array)",
                    ["idiomas"] = "Idiomas - Languages spoken (array)",
                    ["numero_celular"] = "NumeroCelular - Mobile/cell phone number",
                    ["url_foto"] = "UrlFoto - Photo URL",
                    ["notas"] = "Notas - Additional notes",
                    ["createdDate"] = "CreatedDate - Record creation date",
                    ["type"] = "Type - Record type identifier"
                };
                
                StringBuilder schemaBuilder = new StringBuilder();
                schemaBuilder.AppendLine("FamilyData Schema - Cosmos DB JSON Field Mapping:");
                schemaBuilder.AppendLine();

                foreach (var kvp in cosmosDbFieldMapping)
                {
                    string jsonPropertyName = kvp.Key;
                    string description = kvp.Value;
                    
                    // Format for SQL generation: ["json_property"] = Description
                    schemaBuilder.AppendLine($"[\"{jsonPropertyName}\"] = {description}");
                }

                schemaBuilder.AppendLine();
                schemaBuilder.AppendLine($"Total Properties: {cosmosDbFieldMapping.Count}");
                schemaBuilder.AppendLine("Note: These are the actual JSON property names used in Cosmos DB");

                return schemaBuilder.ToString();
            }
            catch (Exception ex)
            {
                // Fallback to manual schema if anything fails
                return GetFallbackSchema(ex);
            }
        }

        /// <summary>
        /// Fallback schema with correct JSON property names if anything fails
        /// </summary>
        private static string GetFallbackSchema(Exception ex)
        {
            return $@"
FamilyData Schema - Fallback (Error: {ex.Message})

Cosmos DB JSON Field Mapping:
[""id""] = Id - Unique identifier for the family member record
[""TwinID""] = TwinID - Associated twin identifier (partition key)
[""parentesco""] = Parentesco - Relationship type (padre, madre, hermano, hermana, hijo, hija, etc.)
[""nombre""] = Nombre - First name of the family member
[""apellido""] = Apellido - Last name/surname of the family member
[""email""] = Email - Email address
[""telefono""] = Telefono - Phone number
[""fecha_nacimiento""] = FechaNacimiento - Date of birth
[""nombre_twin""] = NombreTwin - Twin's name reference
[""direccion_completa""] = DireccionCompleta - Complete address
[""pais_nacimiento""] = PaisNacimiento - Country of birth
[""nacionalidad""] = Nacionalidad - Nationality
[""genero""] = Genero - Gender
[""ocupacion""] = Ocupacion - Occupation/profession
[""intereses""] = Intereses - Interests and hobbies (array)
[""idiomas""] = Idiomas - Languages spoken (array)
[""numero_celular""] = NumeroCelular - Mobile/cell phone number
[""url_foto""] = UrlFoto - Photo URL
[""notas""] = Notas - Additional notes
[""createdDate""] = CreatedDate - Record creation date
[""type""] = Type - Record type identifier

Total Properties: 21
Note: These are the actual JSON property names used in Cosmos DB";
        }
    }
}