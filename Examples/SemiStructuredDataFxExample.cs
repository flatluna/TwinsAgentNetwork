using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TwinAgentsNetwork.Examples
{
    /// <summary>
    /// Example usage of the SemiStructuredDataFx Azure Function
    /// Demonstrates how to process semi-structured data and save to Cosmos DB
    /// </summary>
    public class SemiStructuredDataFxExample
    {
        private readonly HttpClient _httpClient;
        private readonly string _functionBaseUrl;

        public SemiStructuredDataFxExample(string functionBaseUrl = "http://localhost:7071")
        {
            _httpClient = new HttpClient();
            _functionBaseUrl = functionBaseUrl;
        }

        public static async Task RunExample()
        {
            var example = new SemiStructuredDataFxExample();
            
            Console.WriteLine("=== SEMI-STRUCTURED DATA AZURE FUNCTION EXAMPLES ===\n");

            // Example 1: Process JSON document data
            await example.ProcessJsonDocumentExample();
            
            // Example 2: Process insurance document (using your provided data)
            await example.ProcessInsuranceDocumentExample();
            
            // Example 3: Retrieve processed documents
            await example.RetrieveDocumentsExample();
        }

        /// <summary>
        /// Example 1: Process a JSON document
        /// </summary>
        public async Task ProcessJsonDocumentExample()
        {
            Console.WriteLine("?? Example 1: Processing JSON Document");
            
            var jsonData = @"{
                ""personalInfo"": {
                    ""name"": ""Jorge Perez Luna"",
                    ""address"": ""112 Loch Lomond St, Hutto, TX 78634"",
                    ""occupation"": ""Software Engineer""
                },
                ""financial"": {
                    ""annualIncome"": 85000,
                    ""creditScore"": 780,
                    ""insurancePremium"": 2350.00
                },
                ""properties"": [
                    {
                        ""type"": ""primary_residence"",
                        ""value"": 368000,
                        ""mortgageLender"": ""Wells Fargo""
                    }
                ]
            }";

            var requestData = new
            {
                twinId = "388a31e7-d408-40f0-844c-4d2efedaa836",
                semiStructuredData = jsonData,
                dataFormat = "json",
                language = "English",
                analysisType = "insights",
                documentType = "personal_financial_profile",
                fileName = "financial_profile.json",
                filePath = "personal/financial",
                containerName = "388a31e7-d408-40f0-844c-4d2efedaa836",
                mimeType = "application/json",
                metadata = new
                {
                    source = "user_input",
                    category = "financial_data",
                    priority = "high"
                }
            };

            await ProcessDocumentRequest(requestData, "JSON Document");
        }

        /// <summary>
        /// Example 2: Process insurance document (using your provided sample data)
        /// </summary>
        public async Task ProcessInsuranceDocumentExample()
        {
            Console.WriteLine("\n?? Example 2: Processing Insurance Document");
            
            var insuranceTextContent = @"GEICO Insurance Agency, LLC Issued by RANCHERS AND FARMERS MUTUAL INSURANCE COMPANY P.O. Box 5300 Binghamton, NY 13902-9953 Tel. (866) 372-8903 Fax (877) 273-2984
Insured Name and Mailing Address: JORGE PEREZ LUNA ANGELES PEREZ 112 LOCH LOMOND ST HUTTO, TX 78634-
Evidence of Insurance For Policy Number 41587244 This policy covers the listed location(s) from: 12:01 AM July 1, 2026 through 12:01 AM July 1, 2026 (local time)
Send payment to: PO Box 1409 NEWARK, NJ 07101-1409
Insured Location 112 LOCH LOMOND ST HUTTO, TX 78634- Residence: Primary home
Deductible: $1000 Wind/Hail Deductible: 1% ($3680.00) Earthquake Deductible: 10% ($36,800)
Coverage
Limit
Section I - Property
A. Dwelling
$368,000
B. Other Structures
$36,800
C. Personal Property
$184,000
D. Loss of Use
$110,400
Section II - Liability
E. Personal Liability
$300,000
F. Medical Payments to Others
$1,000
Total Policy Premium
$2350.00
Total Amount Due
$0.00
Total Amount Paid
*$2350.00";

            var requestData = new
            {
                twinId = "388a31e7-d408-40f0-844c-4d2efedaa836",
                semiStructuredData = insuranceTextContent,
                dataFormat = "mixed",
                language = "Spanish",
                analysisType = "content",
                documentId = "53c1fd19-a5fb-44d9-97c6-526707df4833",
                documentType = "TITULO_PROPIEDAD",
                fileName = "HomeInsurance.pdf",
                filePath = "homes/f4e7d15b-483a-4e1e-bb85-0b17aff47a31/titulo_propiedad",
                containerName = "388a31e7-d408-40f0-844c-4d2efedaa836",
                mimeType = "application/pdf",
                fileSize = 245760L,
                metadata = new
                {
                    originalDocumentType = "TITULO_PROPIEDAD",
                    category = "CASA_VIVIENDA",
                    description = "Documento legal que prueba la propiedad - 112 Loch Lomond St, Hutto, TX 78634, USA, Hutto",
                    totalPages = 2,
                    documentUrl = "https://flatbitdatalake.dfs.core.windows.net/388a31e7-d408-40f0-844c-4d2efedaa836/homes/f4e7d15b-483a-4e1e-bb85-0b17aff47a31/titulo_propiedad/HomeInsurance.pdf",
                    processingSource = "document_intelligence"
                }
            };

            await ProcessDocumentRequest(requestData, "Insurance Document");
        }

        /// <summary>
        /// Example 3: Retrieve processed documents for a Twin ID
        /// </summary>
        public async Task RetrieveDocumentsExample()
        {
            Console.WriteLine("\n?? Example 3: Retrieving Processed Documents");
            
            try
            {
                var twinId = "388a31e7-d408-40f0-844c-4d2efedaa836";
                var url = $"{_functionBaseUrl}/api/twin-semistructured/{twinId}";
                
                var response = await _httpClient.GetAsync(url);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("? Successfully retrieved documents:");
                    
                    // Pretty print the JSON response
                    var jsonDocument = JsonDocument.Parse(responseContent);
                    var formattedJson = JsonSerializer.Serialize(jsonDocument, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                    
                    Console.WriteLine(formattedJson);
                }
                else
                {
                    Console.WriteLine($"? Failed to retrieve documents: {response.StatusCode}");
                    Console.WriteLine(responseContent);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error retrieving documents: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to process a document request
        /// </summary>
        private async Task ProcessDocumentRequest(object requestData, string exampleName)
        {
            try
            {
                var json = JsonSerializer.Serialize(requestData, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true 
                });
                
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var url = $"{_functionBaseUrl}/api/twin-semistructured/process";
                
                Console.WriteLine($"?? Sending request to: {url}");
                
                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"? {exampleName} processed successfully!");
                    
                    // Parse and display key information from response
                    var jsonResponse = JsonDocument.Parse(responseContent);
                    var root = jsonResponse.RootElement;
                    
                    if (root.TryGetProperty("documentId", out var docId))
                    {
                        Console.WriteLine($"?? Document ID: {docId.GetString()}");
                    }
                    
                    if (root.TryGetProperty("cosmosDb", out var cosmosDb) && 
                        cosmosDb.TryGetProperty("saved", out var saved) && 
                        saved.GetBoolean())
                    {
                        Console.WriteLine("?? Successfully saved to Cosmos DB");
                    }
                    
                    if (root.TryGetProperty("analysisResult", out var analysis) &&
                        analysis.TryGetProperty("aiAnalysis", out var aiAnalysis))
                    {
                        var analysisText = aiAnalysis.GetString();
                        var preview = analysisText?.Length > 200 ? 
                            analysisText.Substring(0, 200) + "..." : analysisText;
                        Console.WriteLine($"?? AI Analysis Preview: {preview}");
                    }
                }
                else
                {
                    Console.WriteLine($"? {exampleName} processing failed: {response.StatusCode}");
                    Console.WriteLine(responseContent);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error processing {exampleName}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Console application to test the Semi-Structured Data Azure Function
    /// </summary>
    public class SemiStructuredDataFxTestProgram
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("?? Testing Semi-Structured Data Azure Function");
            Console.WriteLine("===============================================\n");

            await SemiStructuredDataFxExample.RunExample();

            Console.WriteLine("\n?? API Endpoints Available:");
            Console.WriteLine("POST /api/twin-semistructured/process - Process semi-structured data");
            Console.WriteLine("GET  /api/twin-semistructured/{twinId} - Retrieve documents by Twin ID");

            Console.WriteLine("\n?? Usage Notes:");
            Console.WriteLine("1. The function processes semi-structured data using AI analysis");
            Console.WriteLine("2. Results are automatically saved to Cosmos DB container: TwinSemiStructured");
            Console.WriteLine("3. Data is partitioned by Twin ID for optimal performance");
            Console.WriteLine("4. Supports multiple data formats: JSON, XML, CSV, mixed");
            Console.WriteLine("5. Provides multi-language analysis support");

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}