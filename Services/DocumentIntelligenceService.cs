using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using TwinAgentsLibrary.Services;
using TwinAgentsNetwork.Services;

namespace TwinFx.Services;

/// <summary>
/// Azure Document Intelligence Service for processing invoices and documents
/// ========================================================================
/// 
/// Service specializing in extracting structured data from invoices using Azure AI Document Intelligence.
/// Complements ProcessDocumentDataAgent by providing OCR and structured field extraction.
/// 
/// Features:
/// - Azure AI Document Intelligence integration
/// - Prebuilt invoice model support
/// - Structured field extraction with confidence scores
/// - Multiple document format support (PDF, images)
/// - Comprehensive error handling and logging
/// - Integration with TwinFx ecosystem
/// 
/// Capabilities:
/// 1. Invoice data extraction with prebuilt models
/// 2. Custom document analysis
/// 3. Table and field extraction
/// 4. Confidence scoring for reliability
/// 5. Multiple output formats
/// 
/// Author: TwinFx Project
/// Date: January 15, 2025
/// </summary>
public class DocumentIntelligenceService
{
    private readonly ILogger<DocumentIntelligenceService> _logger;
    private readonly IConfiguration _configuration;
    private readonly DocumentIntelligenceClient _client;
    private readonly DataLakeClientFactory _dataLakeFactory; 
    
    public DocumentIntelligenceService(ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        _logger = loggerFactory.CreateLogger<DocumentIntelligenceService>();
        _configuration = configuration;

        try
        {
            // Get configuration values
            var endpoint = GetConfigurationValue("DocumentIntelligence:Endpoint");
            var apiKey = GetConfigurationValue("DocumentIntelligence:ApiKey");

            _logger.LogInformation("🚀 Initializing Document Intelligence Service");
            _logger.LogInformation($"🔧 Using endpoint: {endpoint}");

            // Initialize Azure Document Intelligence client
            var credential = new AzureKeyCredential(apiKey);
            _client = new DocumentIntelligenceClient(new Uri(endpoint), credential);

            // Initialize Azure Data Lake Storage for file access
            _dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
             

            _logger.LogInformation("✅ Document Intelligence Service initialized successfully!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Document Intelligence Service");
            throw;
        }
    }

    /// <summary>
    /// Extract structured data from invoice using prebuilt invoice model
    /// </summary>
    /// <param name="containerName">Container name where the document is stored</param>
    /// <param name="filePath">Path to the document within the container</param>
    /// <param name="fileName">Original file name for reference</param>
    /// <returns>Structured invoice data with confidence scores</returns>
    public async Task<InvoiceAnalysisResult> ExtractInvoiceDataAsync(string containerName, string filePath, string fileName)
    {
        _logger.LogInformation($"📄 Starting invoice data extraction from container: {containerName}, path: {filePath}, file: {fileName}");

        try
        {
            // Step 1: Test DataLake configuration first
            _logger.LogInformation("🔍 Step 1: Testing DataLake configuration...");
            await TestDataLakeConfigurationAsync(containerName);

            // Step 2: Create DataLake client for the container
            _logger.LogInformation("🔧 Step 2: Creating DataLake client...");
            var dataLakeClient = _dataLakeFactory.CreateClient(containerName);
            
            // Step 3: Test the connection
            _logger.LogInformation("🧪 Step 3: Testing DataLake connection...");
            var connectionSuccess = await dataLakeClient.TestConnectionAsync();
            if (!connectionSuccess)
            {
                return new InvoiceAnalysisResult
                {
                    Success = false,
                    ErrorMessage = "DataLake connection test failed. Please check Azure Storage credentials.",
                    ProcessedAt = DateTime.UtcNow,
                    SourceUri = $"{containerName}/{filePath}/{fileName}"
                };
            }

            filePath = filePath + "/" + fileName;
            
            // Step 4: Get SAS URL for the document
            _logger.LogInformation("🔗 Step 4: Generating SAS URL...");
            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(1));
            
            if (string.IsNullOrEmpty(sasUrl))
            {
                return new InvoiceAnalysisResult
                {
                    Success = false,
                    ErrorMessage = $"Could not generate SAS URL for file: {filePath} in container: {containerName}",
                    ProcessedAt = DateTime.UtcNow,
                    SourceUri = $"{containerName}/{filePath}"
                };
            }

            _logger.LogInformation($"🔗 Generated SAS URL for document: {filePath}");

            // Analyze document using prebuilt invoice model with URL
            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed, 
                "prebuilt-invoice", 
                new Uri(sasUrl));

            var result = operation.Value;

            _logger.LogInformation($"📋 Document analysis completed. Found {result.Documents.Count} document(s)");

            // Process the first document (invoices typically have one document)
            if (result.Documents.Count == 0)
            {
                return new InvoiceAnalysisResult
                {
                    Success = false,
                    ErrorMessage = "No documents found in the provided file",
                    ProcessedAt = DateTime.UtcNow,
                    SourceUri = $"{containerName}/{filePath}"
                };
            }

            var document = result.Documents[0];
            var invoiceData = ExtractInvoiceFields(document);

            // Also extract tables if present
            var tables = ExtractTables(result);

            var analysisResult = new InvoiceAnalysisResult
            {
                Success = true,
                InvoiceData = invoiceData,
                Tables = tables,
                RawDocumentFields = document.Fields.ToDictionary(
                    kvp => kvp.Key, 
                    kvp => new DocumentFieldInfo
                    {
                        Value = GetFieldValue(kvp.Value),
                        Confidence = kvp.Value.Confidence ?? 0.0f,
                        FieldType = GetFieldType(kvp.Value)
                    }),
                ProcessedAt = DateTime.UtcNow,
                SourceUri = $"{containerName}/{filePath}",
                TotalPages = result.Pages?.Count ?? 0
            };

            _logger.LogInformation($"✅ Invoice extraction completed successfully with {invoiceData.LineItems.Count} line items");

            return analysisResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Error extracting invoice data from {containerName}/{filePath}");

            return new InvoiceAnalysisResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessedAt = DateTime.UtcNow,
                SourceUri = $"{containerName}/{filePath}"
            };
        }
    }

    /// <summary>
    /// Extract structured data from invoice using file stream
    /// </summary>
    /// <param name="documentStream">Stream containing the invoice document</param>
    /// <param name="fileName">Original file name for reference</param>
    /// <returns>Structured invoice data with confidence scores</returns>
    public async Task<InvoiceAnalysisResult> ExtractInvoiceDataAsync(Stream documentStream, string fileName)
    {
        _logger.LogInformation($"📄 Starting invoice data extraction from file: {fileName}");

        try
        {
            // Analyze document using prebuilt-layout model with stream
            var content = BinaryData.FromStream(documentStream);
            
            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed, 
                "prebuilt-layout", 
                content);

            var result = operation.Value;

            _logger.LogInformation($"📋 Document analysis completed. Found {result.Documents.Count} document(s)");

            // Process the first document (invoices typically have one document)
            if (result.Documents.Count == 0)
            {
                return new InvoiceAnalysisResult
                {
                    Success = false,
                    ErrorMessage = "No documents found in the provided file",
                    ProcessedAt = DateTime.UtcNow,
                    SourceUri = fileName
                };
            }

            var document = result.Documents[0];
            var invoiceData = ExtractInvoiceFields(document);

            // Also extract tables if present
            var tables = ExtractTables(result);

            var analysisResult = new InvoiceAnalysisResult
            {
                Success = true,
                InvoiceData = invoiceData,
                Tables = tables,
                RawDocumentFields = document.Fields.ToDictionary(
                    kvp => kvp.Key, 
                    kvp => new DocumentFieldInfo
                    {
                        Value = GetFieldValue(kvp.Value),
                        Confidence = kvp.Value.Confidence ?? 0.0f,
                        FieldType = GetFieldType(kvp.Value)
                    }),
                ProcessedAt = DateTime.UtcNow,
                SourceUri = fileName,
                TotalPages = result.Pages?.Count ?? 0
            };

            _logger.LogInformation($"✅ Invoice extraction completed successfully with {invoiceData.LineItems.Count} line items");

            return analysisResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Error extracting invoice data from {fileName}");

            return new InvoiceAnalysisResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessedAt = DateTime.UtcNow,
                SourceUri = fileName
            };
        }
    }

    /// <summary>
    /// Analyze any document type using layout model from URI
    /// </summary>
    /// <param name="documentUri">URI to the document</param>
    /// <returns>General document analysis result</returns>
    public async Task<DocumentAnalysisResult> AnalyzeDocumentAsync(string documentUri)
    {
        _logger.LogInformation($"📄 Starting general document analysis from URI: {documentUri}");

        try
        {
            // Analyze document using layout model for general structure
            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed, 
                "prebuilt-layout",
                new Uri(documentUri));

            var result = operation.Value;

            _logger.LogInformation($"📋 Document analysis completed. Found {result.Pages?.Count ?? 0} page(s)");

            // Extract text content and tables
            var textContent = ExtractTextContent(result);
            var tables = ExtractTables(result);

            var analysisResult = new DocumentAnalysisResult
            {
                Success = true,
                TextContent = textContent,
                Tables = tables,
                ProcessedAt = DateTime.UtcNow,
                SourceUri = documentUri.ToString(),
                TotalPages = result.Pages?.Count ?? 0
            };

            _logger.LogInformation($"✅ Document analysis completed successfully with {tables.Count} table(s)");

            return analysisResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Error analyzing document from {documentUri}");

            return new DocumentAnalysisResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessedAt = DateTime.UtcNow,
                SourceUri = documentUri.ToString()
            };
        }
    }

    /// <summary>
    /// Analyze document with page-level details using layout model
    /// </summary>
    /// <param name="documentUri">URI to the document</param>
    /// <returns>General document analysis result with page information</returns>
    public async Task<DocumentAnalysisResult> AnalyzeDocumentWithPagesAsync(string documentUri)
    {
        _logger.LogInformation($"📄 Starting general document analysis from URI: {documentUri}");

        try
        {
            // Analyze document using layout model for general structure
            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-layout",
                new Uri(documentUri));

            var result = operation.Value;
            
            _logger.LogInformation($"📋 Document analysis completed. Found {result.Pages?.Count ?? 0} page(s)");

            // Extract text content and tables
            var pagesContent = ExtractTextContentPages(result);
            var tables = ExtractTables(result);

            var analysisResult = new DocumentAnalysisResult
            {
                Success = true,
                DocumentPages = pagesContent,
                Tables = tables,
                ProcessedAt = DateTime.UtcNow,
                SourceUri = documentUri.ToString(),
                TotalPages = result.Pages?.Count ?? 0
            };

            _logger.LogInformation($"✅ Document analysis completed successfully with {tables.Count} table(s)");

            return analysisResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Error analyzing document from {documentUri}");

            return new DocumentAnalysisResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessedAt = DateTime.UtcNow,
                SourceUri = documentUri.ToString()
            };
        }
    }

    /// <summary>
    /// Extract structured data from education documents (diplomas, certificates, transcripts) using prebuilt layout model
    /// </summary>
    /// <param name="containerName">Container name where the document is stored</param>
    /// <param name="filePath">Path to the document within the container</param>
    /// <param name="fileName">Original file name for reference</param>
    /// <returns>Structured education data with confidence scores</returns>
    public async Task<EducationAnalysisResult> ExtractEducationDataAsync(string containerName, string filePath, string fileName)
    {
        _logger.LogInformation($"🎓 Starting education data extraction from container: {containerName}, path: {filePath}, file: {fileName}");

        try
        {
            // Step 1: Test DataLake configuration first
            _logger.LogInformation("🔍 Step 1: Testing DataLake configuration...");
            await TestDataLakeConfigurationAsync(containerName);

            // Step 2: Create DataLake client for the container
            _logger.LogInformation("🔧 Step 2: Creating DataLake client...");
            var dataLakeClient = _dataLakeFactory.CreateClient(containerName);
            
            // Step 3: Test the connection
            _logger.LogInformation("🧪 Step 3: Testing DataLake connection...");
            var connectionSuccess = await dataLakeClient.TestConnectionAsync();
            if (!connectionSuccess)
            {
                return new EducationAnalysisResult
                {
                    Success = false,
                    ErrorMessage = "DataLake connection test failed. Please check Azure Storage credentials.",
                    ProcessedAt = DateTime.UtcNow,
                    SourceUri = $"{containerName}/{filePath}/{fileName}"
                };
            }

            filePath = filePath + "/" + fileName;

            // Step 4: Get SAS URL for the document
            _logger.LogInformation("🔗 Step 4: Generating SAS URL...");
            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(1));
            
            if (string.IsNullOrEmpty(sasUrl))
            {
                return new EducationAnalysisResult
                {
                    Success = false,
                    ErrorMessage = $"Could not generate SAS URL for file: {filePath} in container: {containerName}",
                    ProcessedAt = DateTime.UtcNow,
                    SourceUri = $"{containerName}/{filePath}"
                };
            }

            _logger.LogInformation($"🔗 Generated SAS URL for education document: {filePath}");

            // Analyze document using prebuilt-layout model with URL
            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed, 
                "prebuilt-layout", 
                new Uri(sasUrl));

            var result = operation.Value;

            _logger.LogInformation($"📋 Document analysis completed. Found {result.Documents?.Count ?? 0} document(s)");

            // Extract education-specific data
            var educationData = ExtractEducationFields(result);

            // Also extract tables if present (for transcripts with grades)
            var tables = ExtractTables(result);

            // Extract text content for further analysis
            var textContent = ExtractTextContent(result);

            var analysisResult = new EducationAnalysisResult
            {
                Success = true,
                EducationData = educationData,
                Tables = tables,
                TextContent = textContent,
                RawDocumentFields = result.Documents?.FirstOrDefault()?.Fields?.ToDictionary(
                    kvp => kvp.Key, 
                    kvp => new DocumentFieldInfo
                    {
                        Value = GetFieldValue(kvp.Value),
                        Confidence = kvp.Value.Confidence ?? 0.0f,
                        FieldType = GetFieldType(kvp.Value)
                    }) ?? new Dictionary<string, DocumentFieldInfo>(),
                ProcessedAt = DateTime.UtcNow,
                SourceUri = $"{containerName}/{filePath}",
                TotalPages = result.Pages?.Count ?? 0
            };

            _logger.LogInformation($"✅ Education extraction completed successfully");

            return analysisResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Error extracting education data from {containerName}/{filePath}");

            return new EducationAnalysisResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessedAt = DateTime.UtcNow,
                SourceUri = $"{containerName}/{filePath}"
            };
        }
    }

    /// <summary>
    /// Extract structured data from education document using file stream
    /// </summary>
    /// <param name="documentStream">Stream containing the education document</param>
    /// <param name="fileName">Original file name for reference</param>
    /// <returns>Structured education data with confidence scores</returns>
    public async Task<EducationAnalysisResult> ExtractEducationDataAsync(Stream documentStream, string fileName)
    {
        _logger.LogInformation($"🎓 Starting education data extraction from file: {fileName}");

        try
        {
            // Analyze document using prebuilt-layout model with stream
            var content = BinaryData.FromStream(documentStream);
            
            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed, 
                "prebuilt-layout", 
                content);

            var result = operation.Value;

            _logger.LogInformation($"📋 Document analysis completed. Found {result.Documents?.Count ?? 0} document(s)");

            // Extract education-specific data
            var educationData = ExtractEducationFields(result);

            // Also extract tables if present
            var tables = ExtractTables(result);

            // Extract text content
            var textContent = ExtractTextContent(result);

            var analysisResult = new EducationAnalysisResult
            {
                Success = true,
                EducationData = educationData,
                Tables = tables,
                TextContent = textContent,
                RawDocumentFields = result.Documents?.FirstOrDefault()?.Fields?.ToDictionary(
                    kvp => kvp.Key, 
                    kvp => new DocumentFieldInfo
                    {
                        Value = GetFieldValue(kvp.Value),
                        Confidence = kvp.Value.Confidence ?? 0.0f,
                        FieldType = GetFieldType(kvp.Value)
                    }) ?? new Dictionary<string, DocumentFieldInfo>(),
                ProcessedAt = DateTime.UtcNow,
                SourceUri = fileName,
                TotalPages = result.Pages?.Count ?? 0
            };

            _logger.LogInformation($"✅ Education extraction completed successfully");

            return analysisResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Error extracting education data from {fileName}");

            return new EducationAnalysisResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessedAt = DateTime.UtcNow,
                SourceUri = fileName
            };
        }
    }

    /// <summary>
    /// Extract education-specific fields from analyzed document
    /// </summary>
    /// <param name="result">Document analysis result from Document Intelligence</param>
    /// <returns>Structured education data</returns>
    private StructuredEducationData ExtractEducationFields(AnalyzeResult result)
    {
        var educationData = new StructuredEducationData();

        try
        {
            // Get the first document or work with the raw content
            var document = result.Documents?.FirstOrDefault();
            var textContent = result.Content ?? string.Empty;
            
            _logger.LogInformation("🔍 Analyzing education document content...");

            // Use text analysis to extract education-specific information
            educationData = AnalyzeEducationText(textContent);

            // If we have structured fields from the document, use them to enhance the data
            if (document?.Fields != null)
            {
                _logger.LogInformation("📋 Found structured fields, enhancing education data...");
                
                // Look for common education-related fields
                ExtractCommonEducationFields(document.Fields, educationData);
            }

            _logger.LogInformation($"📊 Extracted education data: Institution={educationData.InstitutionName}, Degree={educationData.DegreeTitle}, GPA={educationData.GPA}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error extracting education fields");
        }

        return educationData;
    }

    /// <summary>
    /// Analyze text content to extract education information using pattern matching
    /// </summary>
    /// <param name="textContent">Raw text content from the document</param>
    private StructuredEducationData AnalyzeEducationText(string textContent)
    {
        var educationData = new StructuredEducationData();
        var lines = textContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        _logger.LogInformation("🔍 Analyzing text content with {LineCount} lines", lines.Length);

        try
        {
            // Common patterns for education documents
            var universityPatterns = new[] { "university", "universidad", "college", "instituto", "institute", "school", "escuela" };
            var degreePatterns = new[] { "bachelor", "master", "phd", "doctorate", "licenciatura", "maestría", "doctorado", "diploma", "certificate", "certificado" };
            var gpaPatterns = new[] { "gpa", "promedio", "average", "nota", "calificación", "grade" };
            var datePatterns = new[] { @"\d{4}", @"\d{1,2}/\d{1,2}/\d{4}", @"\d{1,2}-\d{1,2}-\d{4}" };

            foreach (var line in lines)
            {
                var lineLower = line.ToLowerInvariant().Trim();
                
                // Extract institution name
                if (string.IsNullOrEmpty(educationData.InstitutionName))
                {
                    foreach (var pattern in universityPatterns)
                    {
                        if (lineLower.Contains(pattern) && line.Length > 10 && line.Length < 100)
                        {
                            educationData.InstitutionName = line.Trim();
                            educationData.InstitutionConfidence = 0.8f;
                            break;
                        }
                    }
                }

                // Extract degree information
                if (string.IsNullOrEmpty(educationData.DegreeTitle))
                {
                    foreach (var pattern in degreePatterns)
                    {
                        if (lineLower.Contains(pattern) && line.Length > 5 && line.Length < 150)
                        {
                            educationData.DegreeTitle = line.Trim();
                            educationData.DegreeConfidence = 0.8f;
                            break;
                        }
                    }
                }

                // Extract GPA/grades
                if (string.IsNullOrEmpty(educationData.GPA))
                {
                    foreach (var pattern in gpaPatterns)
                    {
                        if (lineLower.Contains(pattern))
                        {
                            // Look for numeric values in the line
                            var matches = System.Text.RegularExpressions.Regex.Matches(line, @"\d+\.?\d*");
                            foreach (System.Text.RegularExpressions.Match match in matches)
                            {
                                if (float.TryParse(match.Value, out var gpaValue) && gpaValue >= 0 && gpaValue <= 5)
                                {
                                    educationData.GPA = match.Value;
                                    educationData.GPAConfidence = 0.7f;
                                    break;
                                }
                            }
                        }
                    }
                }

                // Extract graduation date
                if (string.IsNullOrEmpty(educationData.GraduationDate))
                {
                    foreach (var pattern in datePatterns)
                    {
                        var matches = System.Text.RegularExpressions.Regex.Matches(line, pattern);
                        foreach (System.Text.RegularExpressions.Match match in matches)
                        {
                            educationData.GraduationDate = match.Value;
                            educationData.GraduationDateConfidence = 0.6f;
                            break;
                        }
                    }
                }

                // Extract field of study (look for common academic fields)
                if (string.IsNullOrEmpty(educationData.FieldOfStudy))
                {
                    var fieldPatterns = new[] { "engineering", "ingeniería", "computer science", "computación", "business", "negocios", "medicine", "medicina", "law", "derecho", "arts", "artes" };
                    foreach (var pattern in fieldPatterns)
                    {
                        if (lineLower.Contains(pattern) && line.Length > 5 && line.Length < 100)
                        {
                            educationData.FieldOfStudy = line.Trim();
                            educationData.FieldOfStudyConfidence = 0.7f;
                            break;
                        }
                    }
                }
            }

            // Extract student name (usually at the top of the document)
            var topLines = lines.Take(10);
            foreach (var line in topLines)
            {
                if (string.IsNullOrEmpty(educationData.StudentName) && 
                    line.Length > 5 && line.Length < 50 && 
                    System.Text.RegularExpressions.Regex.IsMatch(line, @"^[A-Za-z\s\.]+$"))
                {
                    educationData.StudentName = line.Trim();
                    educationData.StudentNameConfidence = 0.6f;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error during text analysis");
        }

        return educationData;
    }

    /// <summary>
    /// Extract common education fields from structured document fields
    /// </summary>
    /// <param name="fields">Document fields from Azure Document Intelligence</param>
    /// <param name="educationData">Education data to enhance</param>
    private void ExtractCommonEducationFields(IReadOnlyDictionary<string, DocumentField> fields, StructuredEducationData educationData)
    {
        try
        {
            // Map common field names to education data
            var fieldMappings = new Dictionary<string, Action<DocumentField>>
            {
                ["StudentName"] = field => { educationData.StudentName = GetStringFieldValue(field); educationData.StudentNameConfidence = field.Confidence ?? 0.0f; },
                ["Name"] = field => { if (string.IsNullOrEmpty(educationData.StudentName)) { educationData.StudentName = GetStringFieldValue(field); educationData.StudentNameConfidence = field.Confidence ?? 0.0f; } },
                ["Institution"] = field => { educationData.InstitutionName = GetStringFieldValue(field); educationData.InstitutionConfidence = field.Confidence ?? 0.0f; },
                ["University"] = field => { if (string.IsNullOrEmpty(educationData.InstitutionName)) { educationData.InstitutionName = GetStringFieldValue(field); educationData.InstitutionConfidence = field.Confidence ?? 0.0f; } },
                ["Degree"] = field => { educationData.DegreeTitle = GetStringFieldValue(field); educationData.DegreeConfidence = field.Confidence ?? 0.0f; },
                ["GPA"] = field => { educationData.GPA = GetStringFieldValue(field); educationData.GPAConfidence = field.Confidence ?? 0.0f; },
                ["GraduationDate"] = field => { educationData.GraduationDate = GetDateFieldValue(field)?.ToString("yyyy-MM-dd") ?? GetStringFieldValue(field); educationData.GraduationDateConfidence = field.Confidence ?? 0.0f; },
                ["Date"] = field => { if (string.IsNullOrEmpty(educationData.GraduationDate)) { educationData.GraduationDate = GetDateFieldValue(field)?.ToString("yyyy-MM-dd") ?? GetStringFieldValue(field); educationData.GraduationDateConfidence = field.Confidence ?? 0.0f; } },
                ["FieldOfStudy"] = field => { educationData.FieldOfStudy = GetStringFieldValue(field); educationData.FieldOfStudyConfidence = field.Confidence ?? 0.0f; },
                ["Major"] = field => { if (string.IsNullOrEmpty(educationData.FieldOfStudy)) { educationData.FieldOfStudy = GetStringFieldValue(field); educationData.FieldOfStudyConfidence = field.Confidence ?? 0.0f; } }
            };

            foreach (var kvp in fields)
            {
                if (fieldMappings.TryGetValue(kvp.Key, out var action))
                {
                    action(kvp.Value);
                    _logger.LogInformation("📋 Mapped field {FieldName} = {Value}", kvp.Key, GetStringFieldValue(kvp.Value));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error extracting structured education fields");
        }
    }

    /// <summary>
    /// Extract invoice-specific fields from analyzed document
    /// </summary>
    /// <param name="document">Analyzed document from Document Intelligence</param>
    /// <returns>Structured invoice data</returns>
    private StructuredInvoiceData ExtractInvoiceFields(AnalyzedDocument document)
    {
        var invoiceData = new StructuredInvoiceData();

        try
        {
            // Extract vendor information
            if (document.Fields.TryGetValue("VendorName", out var vendorNameField))
            {
                invoiceData.VendorName = GetStringFieldValue(vendorNameField);
                invoiceData.VendorNameConfidence = vendorNameField.Confidence ?? 0.0f;
            }

            if (document.Fields.TryGetValue("VendorAddress", out var vendorAddressField))
            {
                invoiceData.VendorAddress = GetStringFieldValue(vendorAddressField);
            }

            // Extract customer information
            if (document.Fields.TryGetValue("CustomerName", out var customerNameField))
            {
                invoiceData.CustomerName = GetStringFieldValue(customerNameField);
                invoiceData.CustomerNameConfidence = customerNameField.Confidence ?? 0.0f;
            }

            if (document.Fields.TryGetValue("CustomerAddress", out var customerAddressField))
            {
                invoiceData.CustomerAddress = GetStringFieldValue(customerAddressField);
            }

            // Extract invoice metadata
            if (document.Fields.TryGetValue("InvoiceId", out var invoiceIdField))
            {
                invoiceData.InvoiceNumber = GetStringFieldValue(invoiceIdField);
            }

            if (document.Fields.TryGetValue("InvoiceDate", out var invoiceDateField))
            {
                invoiceData.InvoiceDate = GetDateFieldValue(invoiceDateField);
            }

            if (document.Fields.TryGetValue("DueDate", out var dueDateField))
            {
                invoiceData.DueDate = GetDateFieldValue(dueDateField);
            }

            // Extract financial totals
            if (document.Fields.TryGetValue("SubTotal", out var subTotalField))
            {
                invoiceData.SubTotal = GetCurrencyFieldValue(subTotalField);
                invoiceData.SubTotalConfidence = subTotalField.Confidence ?? 0.0f;
            }

            if (document.Fields.TryGetValue("TotalTax", out var totalTaxField))
            {
                invoiceData.TotalTax = GetCurrencyFieldValue(totalTaxField);
            }

            if (document.Fields.TryGetValue("InvoiceTotal", out var invoiceTotalField))
            {
                invoiceData.InvoiceTotal = GetCurrencyFieldValue(invoiceTotalField);
                invoiceData.InvoiceTotalConfidence = invoiceTotalField.Confidence ?? 0.0f;
            }

            // Extract line items
            if (document.Fields.TryGetValue("Items", out var itemsField) && IsListField(itemsField))
            {
                foreach (var itemField in GetFieldList(itemsField))
                {
                    if (IsDictionaryField(itemField))
                    {
                        var lineItem = ExtractLineItem(GetFieldDictionary(itemField));
                        if (lineItem != null)
                        {
                            if(lineItem.Description != "" && lineItem.Description != null)
                            {
                                invoiceData.LineItems.Add(lineItem);
                            }
                             
                        }
                    }
                }
            }

            _logger.LogInformation($"📊 Extracted {invoiceData.LineItems.Count} line items from invoice");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error extracting some invoice fields");
        }

        return invoiceData;
    }

    /// <summary>
    /// Extract line item from document field dictionary
    /// </summary>
    /// <param name="itemFields">Dictionary of fields for a line item</param>
    /// <returns>Structured line item data</returns>
    private InvoiceLineItem? ExtractLineItem(IReadOnlyDictionary<string, DocumentField> itemFields)
    {
        try
        {
            var lineItem = new InvoiceLineItem();

            if (itemFields.TryGetValue("Description", out var descriptionField))
            {
                lineItem.Description = GetStringFieldValue(descriptionField);
                lineItem.DescriptionConfidence = descriptionField.Confidence ?? 0.0f;
            }

            if (itemFields.TryGetValue("Quantity", out var quantityField))
            {
                lineItem.Quantity = GetNumberFieldValue(quantityField);
            }

            if (itemFields.TryGetValue("UnitPrice", out var unitPriceField))
            {
                lineItem.UnitPrice = GetCurrencyFieldValue(unitPriceField);
            }

            if (itemFields.TryGetValue("Amount", out var amountField))
            {
                lineItem.Amount = GetCurrencyFieldValue(amountField);
                lineItem.AmountConfidence = amountField.Confidence ?? 0.0f;
            }

            // Only return line item if we have at least description or amount
            if (!string.IsNullOrEmpty(lineItem.Description) || lineItem.Amount > 0)
            {
                return lineItem;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error extracting line item");
        }

        return null;
    }

    /// <summary>
    /// Extract tables from the document analysis result
    /// </summary>
    /// <param name="result">Document analysis result</param>
    /// <returns>List of extracted tables</returns>
    private List<ExtractedTable> ExtractTables(AnalyzeResult result)
    {
        var tables = new List<ExtractedTable>();

        try
        {
            if (result.Tables != null)
            {
                for (int tableIndex = 0; tableIndex < result.Tables.Count; tableIndex++)
                {
                    var table = result.Tables[tableIndex];
                    var extractedTable = new ExtractedTable
                    {
                        RowCount = table.RowCount,
                        ColumnCount = table.ColumnCount
                    };

                    // Create simple table representation directly from Azure table
                    extractedTable.AsSimpleTable = ConvertAzureTableToSimple(table, tableIndex);

                    tables.Add(extractedTable);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error extracting tables");
        }

        return tables;
    }

    /// <summary>
    /// Convert Azure table to SimpleTable format for easy reading
    /// </summary>
    /// <param name="azureTable">Azure table from Document Intelligence</param>
    /// <param name="tableIndex">Index of table for naming</param>
    /// <returns>Simple table with rows and columns</returns>
    private static SimpleTable ConvertAzureTableToSimple(DocumentTable azureTable, int tableIndex)
    {
        var simpleTable = new SimpleTable
        {
            TableName = $"Table {tableIndex + 1}"
        };

        try
        {
            if (azureTable.Cells.Count == 0)
            {
                return simpleTable;
            }

            // Create a 2D array to organize cells by position
            var grid = new string[azureTable.RowCount, azureTable.ColumnCount];
            
            // Fill the grid with cell content directly from Azure table
            foreach (var cell in azureTable.Cells)
            {
                if (cell.RowIndex < azureTable.RowCount && cell.ColumnIndex < azureTable.ColumnCount)
                {
                    grid[cell.RowIndex, cell.ColumnIndex] = cell.Content ?? string.Empty;
                }
            }

            // Extract headers (first row)
            if (azureTable.RowCount > 0)
            {
                for (int col = 0; col < azureTable.ColumnCount; col++)
                {
                    var headerText = grid[0, col] ?? $"Column {col + 1}";
                    simpleTable.Headers.Add(headerText);
                }
            }

            // Extract data rows (skip first row if it's headers)
            int startRow = azureTable.RowCount > 1 ? 1 : 0;
            for (int row = startRow; row < azureTable.RowCount; row++)
            {
                var rowData = new List<string>();
                for (int col = 0; col < azureTable.ColumnCount; col++)
                {
                    rowData.Add(grid[row, col] ?? string.Empty);
                }
                
                // Only add row if it has some content
                if (rowData.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                {
                    simpleTable.Rows.Add(rowData);
                }
            }

            // If no headers were detected, use the first data row as headers
            if (simpleTable.Headers.All(h => string.IsNullOrWhiteSpace(h)) && simpleTable.Rows.Count > 0)
            {
                simpleTable.Headers = simpleTable.Rows[0];
                simpleTable.Rows.RemoveAt(0);
            }
        }
        catch (Exception)
        {
            // Return empty table structure if conversion fails
            simpleTable.Headers = Enumerable.Range(1, azureTable.ColumnCount)
                .Select(i => $"Column {i}")
                .ToList();
        }

        return simpleTable;
    }

    /// <summary>
    /// Extract text content from document analysis result
    /// </summary>
    /// <param name="result">Document analysis result</param>
    /// <returns>Extracted text content</returns>
    private string ExtractTextContent(AnalyzeResult result)
    {
        try
        {
            if (!string.IsNullOrEmpty(result.Content))
            {
                return result.Content;
            }

            // Fallback: concatenate text from pages
            var textBuilder = new StringBuilder();
            if (result.Pages != null)
            {
                foreach (var page in result.Pages)
                {
                    if (page.Lines != null)
                    {
                        foreach (var line in page.Lines)
                        {
                            textBuilder.AppendLine(line.Content);
                        }
                    }
                }
            }

            return textBuilder.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error extracting text content");
            return string.Empty;
        }
    }
    private List<DocumentPage> ExtractTextContentPages(AnalyzeResult result)
    {
        try
        {
            var documentPages = new List<DocumentPage>();

            if (result.Pages != null)
            {
                for (int pageIndex = 0; pageIndex < result.Pages.Count; pageIndex++)
                {
                    var page = result.Pages[pageIndex];
                    var documentPage = new DocumentPage
                    {
                        PageNumber = pageIndex + 1,
                        LinesText = new List<string>()
                    };

                    // Extract text from page lines
                    if (page.Lines != null)
                    {
                        foreach (var line in page.Lines)
                        {
                            documentPage.LinesText.Add(line.Content ?? string.Empty);
                        }
                    }

                    // Calculate tokens for the entire page (simplified - no external dependency)
                    var allPageText = string.Join(" ", documentPage.LinesText);
                    documentPage.TotalTokens = EstimateTokenCount(allPageText);

                    documentPages.Add(documentPage);
                }
            }
            else if (!string.IsNullOrEmpty(result.Content))
            {
                // Fallback: if no separate pages, create a single page with all content
                var lines = result.Content.Split('\n', StringSplitOptions.None).ToList();
                var documentPage = new DocumentPage
                {
                    PageNumber = 1,
                    LinesText = lines,
                    TotalTokens = EstimateTokenCount(result.Content)
                };
                documentPages.Add(documentPage);
            }

            _logger.LogInformation("📄 Extracted {PageCount} pages with token counts", documentPages.Count);
            
            return documentPages;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error extracting text content pages");
            return new List<DocumentPage>();
        }
    }

    /// <summary>
    /// Estimate token count for text (rough approximation)
    /// </summary>
    private static double EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        
        // Simple approximation: ~4 characters per token on average
        return Math.Ceiling(text.Length / 4.0);
    }
    
    /// <summary>
    /// Helper method to get string value from document field
    /// </summary>
    private static string GetStringFieldValue(DocumentField field)
    {
        if (field.ValueString != null)
            return field.ValueString;
        
        return field.Content ?? string.Empty;
    }

    /// <summary>
    /// Helper method to get date value from document field
    /// </summary>
    private static DateTime? GetDateFieldValue(DocumentField field)
    {
        return field.ValueDate?.DateTime;
    }

    /// <summary>
    /// Helper method to get currency value from document field
    /// </summary>
    private static decimal GetCurrencyFieldValue(DocumentField field)
    {
        if (field.ValueCurrency?.Amount != null)
        {
            try
            {
                return (decimal)field.ValueCurrency.Amount;
            }
            catch
            {
                // Fallback if casting fails
                if (double.TryParse(field.ValueCurrency.Amount.ToString(), out var doubleValue))
                {
                    return (decimal)doubleValue;
                }
            }
        }
        
        return 0m;
    }

    /// <summary>
    /// Helper method to get number value from document field
    /// </summary>
    private static double GetNumberFieldValue(DocumentField field)
    {
        // Try to parse from string content if available
        if (!string.IsNullOrEmpty(field.Content) && double.TryParse(field.Content, out var parsed))
        {
            return parsed;
        }
        
        return 0.0;
    }

    /// <summary>
    /// Helper method to check if field is a list
    /// </summary>
    private static bool IsListField(DocumentField field)
    {
        // In the latest SDK, list fields are accessed differently
        return false; // Simplified for compatibility
    }

    /// <summary>
    /// Helper method to get list from field
    /// </summary>
    private static IEnumerable<DocumentField> GetFieldList(DocumentField field)
    {
        return Array.Empty<DocumentField>();
    }

    /// <summary>
    /// Helper method to check if field is a dictionary
    /// </summary>
    private static bool IsDictionaryField(DocumentField field)
    {
        // In the latest SDK, object fields are accessed differently
        return false; // Simplified for compatibility
    }

    /// <summary>
    /// Helper method to get dictionary from field
    /// </summary>
    private static IReadOnlyDictionary<string, DocumentField> GetFieldDictionary(DocumentField field)
    {
        return new Dictionary<string, DocumentField>();
    }

    /// <summary>
    /// Helper method to get field type as string
    /// </summary>
    private static string GetFieldType(DocumentField field)
    {
        if (field.ValueString != null) return "String";
        if (field.ValueDate != null) return "Date";
        if (field.ValueTime != null) return "Time";
        if (field.ValuePhoneNumber != null) return "PhoneNumber";
        if (field.ValueCurrency != null) return "Currency";
        if (field.ValueAddress != null) return "Address";
        if (field.ValueBoolean != null) return "Boolean";
        if (field.ValueCountryRegion != null) return "CountryRegion";
        return "Unknown";
    }

    /// <summary>
    /// Helper method to get generic field value as string
    /// </summary>
    private static string GetFieldValue(DocumentField field)
    {
        if (field.ValueString != null) return field.ValueString;
        if (field.ValueDate != null) return field.ValueDate.Value.ToString("yyyy-MM-dd");
        if (field.ValueTime != null) return field.ValueTime.Value.ToString();
        if (field.ValuePhoneNumber != null) return field.ValuePhoneNumber;
        if (field.ValueCurrency != null) 
        {
            // Handle CurrencyValue structure
            try
            {
                var currencyValue = field.ValueCurrency;
                var amount = currencyValue.Amount;
                var symbol = currencyValue.CurrencySymbol ?? currencyValue.CurrencyCode ?? "$";
                return $"{symbol}{amount:F2}";
            }
            catch
            {
                // Fallback if the structure is different
                return field.ValueCurrency.ToString() ?? string.Empty;
            }
        }
        if (field.ValueAddress != null) return field.ValueAddress.ToString() ?? string.Empty;
        if (field.ValueBoolean != null) return field.ValueBoolean.Value.ToString();
        if (field.ValueCountryRegion != null) return field.ValueCountryRegion;
        
        return field.Content ?? string.Empty;
    }

    /// <summary>
    /// Get configuration value with error handling
    /// </summary>
    /// <param name="key">Configuration key</param>
    /// <returns>Configuration value</returns>
    private string GetConfigurationValue(string key)
    {
        var value = _configuration[key];
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"Configuration value '{key}' is not set");
        }
        return value;
    }

    /// <summary>
    /// Get simple table data as a formatted string for easy reading
    /// </summary>
    /// <param name="tables">List of extracted tables</param>
    /// <returns>Formatted string representation of all tables</returns>
    public static string GetSimpleTablesAsText(List<ExtractedTable> tables)
    {
        var result = new StringBuilder();

        foreach (var table in tables)
        {
            result.AppendLine($"=== {table.AsSimpleTable.TableName} ===");
            result.AppendLine($"Rows: {table.AsSimpleTable.RowCount}, Columns: {table.AsSimpleTable.ColumnCount}");
            result.AppendLine();

            // Add headers
            if (table.AsSimpleTable.Headers.Count > 0)
            {
                result.AppendLine("Headers:");
                result.AppendLine(string.Join(" | ", table.AsSimpleTable.Headers));
                result.AppendLine(new string('-', string.Join(" | ", table.AsSimpleTable.Headers).Length));
            }

            // Add data rows
            foreach (var row in table.AsSimpleTable.Rows)
            {
                result.AppendLine(string.Join(" | ", row));
            }

            result.AppendLine();
        }

        return result.ToString();
    }

    /// <summary>
    /// Get simple table data as JSON for easy processing
    /// </summary>
    /// <param name="tables">List of extracted tables</param>
    /// <returns>JSON representation of simple tables</returns>
    public static string GetSimpleTablesAsJson(List<ExtractedTable> tables)
    {
        var simpleTables = tables.Select(t => t.AsSimpleTable).ToList();
        return JsonSerializer.Serialize(simpleTables, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Test Azure Storage connection with detailed diagnostics
    /// </summary>
    /// <param name="containerName">Container name to test</param>
    /// <returns>Diagnostic result with connection status and details</returns>
    public async Task<StorageDiagnosticResult> TestStorageConnectionAsync(string containerName)
    {
        var result = new StorageDiagnosticResult { ContainerName = containerName };
        
        try
        {
            _logger.LogInformation("🧪 Starting comprehensive Azure Storage connection test...");
            
            // Step 1: Check configuration
            var accountName = _configuration.GetValue<string>("AZURE_STORAGE_ACCOUNT_NAME");
            var accountKey = _configuration.GetValue<string>("AZURE_STORAGE_ACCOUNT_KEY");
            
            result.AccountName = accountName ?? "NULL";
            result.HasAccountKey = !string.IsNullOrWhiteSpace(accountKey);
            result.AccountKeyLength = accountKey?.Length ?? 0;
            
            _logger.LogInformation($"📋 Configuration Check:");
            _logger.LogInformation($"   • Account Name: {result.AccountName}");
            _logger.LogInformation($"   • Has Account Key: {result.HasAccountKey}");
            _logger.LogInformation($"   • Account Key Length: {result.AccountKeyLength}");
            
            if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(accountKey))
            {
                result.Success = false;
                result.ErrorMessage = "Missing Azure Storage credentials in configuration";
                result.Suggestions.Add("Check AZURE_STORAGE_ACCOUNT_NAME and AZURE_STORAGE_ACCOUNT_KEY in local.settings.json");
                return result;
            }
            
            // Step 2: Test DataLake client creation
            _logger.LogInformation("🔧 Step 2: Testing DataLake client creation...");
            try
            {
                var dataLakeClient = _dataLakeFactory.CreateClient(containerName);
                result.ClientCreated = true;
                _logger.LogInformation("✅ DataLake client created successfully");
                
                // Step 3: Test connection
                _logger.LogInformation("🔗 Step 3: Testing Azure Storage connection...");
                var connectionSuccess = await dataLakeClient.TestConnectionAsync();
                result.ConnectionTested = true;
                result.ConnectionSuccess = connectionSuccess;
                
                if (connectionSuccess)
                {
                    _logger.LogInformation("✅ Azure Storage connection successful!");
                    result.Success = true;
                    
                    // Step 4: Test container operations
                    _logger.LogInformation("📦 Step 4: Testing container operations...");
                    try
                    {
                        var files = await dataLakeClient.ListFilesAsync();
                        result.ContainerAccessible = true;
                        result.FilesFound = files?.Count ?? 0;
                        _logger.LogInformation($"✅ Container accessible, found {result.FilesFound} files");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Container operations failed");
                        result.ContainerAccessible = false;
                        result.Suggestions.Add($"Container '{containerName}' may not exist or be accessible");
                    }
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "Azure Storage connection test failed";
                    result.Suggestions.Add("Verify Azure Storage account name and key are correct");
                    result.Suggestions.Add("Check if storage account exists and is accessible");
                    result.Suggestions.Add("Verify network connectivity to Azure Storage");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ DataLake client creation failed");
                result.ClientCreated = false;
                result.Success = false;
                result.ErrorMessage = ex.Message;
                
                if (ex.Message.Contains("account information"))
                {
                    result.Suggestions.Add("Azure Storage credentials are invalid or expired");
                    result.Suggestions.Add("Verify the storage account key in local.settings.json");
                    result.Suggestions.Add("Check if the storage account name is correct");
                }
                else if (ex.Message.Contains("authentication"))
                {
                    result.Suggestions.Add("Authentication failed - check storage account key");
                }
                else
                {
                    result.Suggestions.Add("Unexpected error during client creation");
                }
            }
            
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Storage diagnostic test failed");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Suggestions.Add("Unexpected error during diagnostic test");
        }
        
        // Log final results
        _logger.LogInformation("📊 Storage Diagnostic Results:");
        _logger.LogInformation($"   🎯 Overall Success: {result.Success}");
        _logger.LogInformation($"   🔧 Client Created: {result.ClientCreated}");
        _logger.LogInformation($"   🔗 Connection Tested: {result.ConnectionTested}");
        _logger.LogInformation($"   ✅ Connection Success: {result.ConnectionSuccess}");
        _logger.LogInformation($"   📦 Container Accessible: {result.ContainerAccessible}");
        _logger.LogInformation($"   📄 Files Found: {result.FilesFound}");
        
        if (!result.Success)
        {
            _logger.LogError($"❌ Error: {result.ErrorMessage}");
            _logger.LogInformation("💡 Suggestions:");
            foreach (var suggestion in result.Suggestions)
            {
                _logger.LogInformation($"   • {suggestion}");
            }
        }
        
        return result;
    }

    /// <summary>
    /// Test DataLake configuration and provide diagnostic information
    /// </summary>
    /// <param name="containerName">Container name to test</param>
    private async Task TestDataLakeConfigurationAsync(string containerName)
    {
        try
        {
            _logger.LogInformation("🔍 Diagnosing DataLake configuration...");
            
            // Check configuration values
            var accountName = _configuration.GetValue<string>("AZURE_STORAGE_ACCOUNT_NAME");
            var accountKey = _configuration.GetValue<string>("AZURE_STORAGE_ACCOUNT_KEY");
            
            _logger.LogInformation($"📋 Configuration Analysis:");
            _logger.LogInformation($"   • Account Name: {accountName ?? "NULL"}");
            _logger.LogInformation($"   • Account Key Length: {accountKey?.Length ?? 0} characters");
            _logger.LogInformation($"   • Container Name: {containerName}");
            
            if (string.IsNullOrWhiteSpace(accountName))
            {
                _logger.LogError("❌ AZURE_STORAGE_ACCOUNT_NAME is missing or empty");
            }
            
            if (string.IsNullOrWhiteSpace(accountKey))
            {
                _logger.LogError("❌ AZURE_STORAGE_ACCOUNT_KEY is missing or empty");
            }
            
            if (accountKey?.Length < 50)
            {
                _logger.LogWarning("⚠️ Account key seems too short - might be invalid");
            }
            
            _logger.LogInformation("✅ Configuration diagnostic completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during configuration diagnostic");
        }
    }

    /// Calculate the number of tokens that the messages would consume.
   
}

/// <summary>
/// Result of invoice analysis using Document Intelligence
/// </summary>
public class InvoiceAnalysisResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public StructuredInvoiceData InvoiceData { get; set; } = new();
    public List<ExtractedTable> Tables { get; set; } = new();
    public Dictionary<string, DocumentFieldInfo> RawDocumentFields { get; set; } = new();
    public DateTime ProcessedAt { get; set; }
    public string SourceUri { get; set; } = string.Empty;
    public int TotalPages { get; set; }
}

/// <summary>
/// Result of general document analysis
/// </summary>
public class DocumentAnalysisResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string TextContent { get; set; } = string.Empty;
    public List<ExtractedTable> Tables { get; set; } = new();
    public DateTime ProcessedAt { get; set; }
    public string SourceUri { get; set; } = string.Empty;
    public int TotalPages { get; set; }

    public List<DocumentPage> DocumentPages { get; set; } = new();
}

/// <summary>
/// Result of education document analysis using Document Intelligence
/// </summary>
public class EducationAnalysisResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public StructuredEducationData EducationData { get; set; } = new();
    public List<ExtractedTable> Tables { get; set; } = new();
    public string TextContent { get; set; } = string.Empty;
    public Dictionary<string, DocumentFieldInfo> RawDocumentFields { get; set; } = new();
    public DateTime ProcessedAt { get; set; }
    public string SourceUri { get; set; } = string.Empty;
    public int TotalPages { get; set; }
}

/// <summary>
/// Structured education data extracted from Document Intelligence
/// </summary>
public class StructuredEducationData
{
    // Student Information
    public string StudentName { get; set; } = string.Empty;
    public float StudentNameConfidence { get; set; }
    public string StudentID { get; set; } = string.Empty;
    public float StudentIDConfidence { get; set; }

    // Institution Information
    public string InstitutionName { get; set; } = string.Empty;
    public float InstitutionConfidence { get; set; }
    public string InstitutionAddress { get; set; } = string.Empty;
    public string InstitutionType { get; set; } = string.Empty; // University, College, Institute, etc.

    // Academic Program Information
    public string DegreeTitle { get; set; } = string.Empty;
    public float DegreeConfidence { get; set; }
    public string DegreeType { get; set; } = string.Empty; // Bachelor, Master, PhD, Certificate, etc.
    public string FieldOfStudy { get; set; } = string.Empty;
    public float FieldOfStudyConfidence { get; set; }
    public string Major { get; set; } = string.Empty;
    public string Minor { get; set; } = string.Empty;

    // Academic Performance
    public string GPA { get; set; } = string.Empty;
    public float GPAConfidence { get; set; }
    public string GPAScale { get; set; } = string.Empty; // 4.0, 5.0, 100, etc.
    public string FinalGrade { get; set; } = string.Empty;
    public string ClassRank { get; set; } = string.Empty;
    public string Honors { get; set; } = string.Empty; // Cum Laude, Magna Cum Laude, etc.

    // Dates
    public string StartDate { get; set; } = string.Empty;
    public float StartDateConfidence { get; set; }
    public string GraduationDate { get; set; } = string.Empty;
    public float GraduationDateConfidence { get; set; }
    public string AcademicYear { get; set; } = string.Empty;

    // Additional Information
    public string Credits { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public string Certification { get; set; } = string.Empty;
    public string AccreditationBody { get; set; } = string.Empty;
    public List<CourseRecord> Courses { get; set; } = new();
    
    // Document Metadata
    public string DocumentType { get; set; } = string.Empty; // Diploma, Certificate, Transcript, etc.
    public string Language { get; set; } = string.Empty;
    public string IssuingAuthority { get; set; } = string.Empty;
}

/// <summary>
/// Individual course record for transcripts
/// </summary>
public class CourseRecord
{
    public string CourseCode { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public string Credits { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public float GradeConfidence { get; set; }
    public string Semester { get; set; } = string.Empty;
    public string AcademicYear { get; set; } = string.Empty;
}

/// <summary>
/// Structured invoice data extracted from Document Intelligence
/// </summary>
public class StructuredInvoiceData
{
    // Vendor Information
    public string VendorName { get; set; } = string.Empty;
    public float VendorNameConfidence { get; set; }
    public string VendorAddress { get; set; } = string.Empty;

    // Customer Information
    public string CustomerName { get; set; } = string.Empty;
    public float CustomerNameConfidence { get; set; }
    public string CustomerAddress { get; set; } = string.Empty;

    // Invoice Metadata
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime? InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }

    // Financial Totals
    public decimal SubTotal { get; set; }
    public float SubTotalConfidence { get; set; }
    public decimal TotalTax { get; set; }
    public decimal InvoiceTotal { get; set; }
    public float InvoiceTotalConfidence { get; set; }

    // Line Items
    public List<InvoiceLineItem> LineItems { get; set; } = new();
}

/// <summary>
/// Individual line item from invoice
/// </summary>
public class InvoiceLineItem
{
    public string Description { get; set; } = string.Empty;
    public float DescriptionConfidence { get; set; }
    public double Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public float AmountConfidence { get; set; }
}

/// <summary>
/// Simple table data structure with rows and columns (no coordinates)
/// </summary>
public class SimpleTable
{
    public string TableName { get; set; } = string.Empty;
    public List<string> Headers { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
    public int RowCount => Rows.Count;
    public int ColumnCount => Headers.Count;
}

/// <summary>
/// Extracted table structure (simplified)
/// </summary>
public class ExtractedTable
{
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    
    // Simple table representation
    public SimpleTable AsSimpleTable { get; set; } = new();
}

/// <summary>
/// Information about a document field
/// </summary>
public class DocumentFieldInfo
{
    public string Value { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public string FieldType { get; set; } = string.Empty;
}

public class DocumentPage
{

    public int PageNumber { get; set; } 

    public List<string> LinesText { get; set; }

    public string OriginalLanguage { get; set; } = "en";

    public string TargetLanguage { get; set; } = "en";

    public double TotalTokens { get; set; }
}
/// <summary>
/// Result of Azure Storage diagnostic test
/// </summary>
public class StorageDiagnosticResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string ContainerName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public bool HasAccountKey { get; set; }
    public int AccountKeyLength { get; set; }
    public bool ClientCreated { get; set; }
    public bool ConnectionTested { get; set; }
    public bool ConnectionSuccess { get; set; }
    public bool ContainerAccessible { get; set; }
    public int FilesFound { get; set; }
    public List<string> Suggestions { get; set; } = new();
    public DateTime TestedAt { get; set; } = DateTime.UtcNow;
}