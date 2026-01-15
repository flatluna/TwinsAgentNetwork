using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.AI.OpenAI;
using OpenAI.Embeddings;

namespace TwinAgentsNetwork.Services
{
    public class DocumentIndex
    {
        private readonly ILogger<DocumentIndex>? _logger;
        private readonly IConfiguration? _configuration;
        private readonly SearchIndexClient? _indexClient;
        private readonly string? _searchEndpoint;
        private readonly string? _searchApiKey;
        private readonly AzureOpenAIClient? _azureOpenAIClient;
        private readonly EmbeddingClient? _embeddingClient;

        // Configuration constants
        private const string IndexName = "document-index";
        private const string VectorSearchProfile = "document-vector-profile";
        private const string HnswAlgorithmConfig = "document-hnsw-config";
        private const int EmbeddingDimensions = 1536;

        public DocumentIndex(ILogger<DocumentIndex> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            // Load Azure Search configuration
            _searchEndpoint = GetConfigurationValue("AZURE_SEARCH_ENDPOINT");
            _searchApiKey = GetConfigurationValue("AZURE_SEARCH_API_KEY");

            _logger?.LogInformation("🔍 DIAGNOSTICS: Azure Search Configuration for DocumentIndex");
            _logger?.LogInformation("   📍 Search Endpoint: {Endpoint}", string.IsNullOrEmpty(_searchEndpoint) ? "NOT SET" : _searchEndpoint);
            _logger?.LogInformation("   🔑 Search API Key: {KeyStatus}", string.IsNullOrEmpty(_searchApiKey) ? "NOT SET" : $"SET ({_searchApiKey?.Length} chars)");

            // Initialize Azure Search client
            if (!string.IsNullOrEmpty(_searchEndpoint) && !string.IsNullOrEmpty(_searchApiKey))
            {
                try
                {
                    var credential = new AzureKeyCredential(_searchApiKey);
                    _indexClient = new SearchIndexClient(new Uri(_searchEndpoint), credential);
                    _logger?.LogInformation("✅ Document Index client initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "❌ Error initializing Azure Search client for Document Index");
                }
            }
            else
            {
                _logger?.LogWarning("⚠️ Azure Search credentials not found for Document Index");
            }

            // Initialize Azure OpenAI client for embeddings
            var openAIEndpoint = GetConfigurationValue("AZURE_OPENAI_ENDPOINT") ?? GetConfigurationValue("AzureOpenAI:Endpoint");
            var openAIApiKey = GetConfigurationValue("AZURE_OPENAI_API_KEY") ?? GetConfigurationValue("AzureOpenAI:ApiKey");

            if (!string.IsNullOrEmpty(openAIEndpoint) && !string.IsNullOrEmpty(openAIApiKey))
            {
                try
                {
                    _azureOpenAIClient = new AzureOpenAIClient(new Uri(openAIEndpoint), new AzureKeyCredential(openAIApiKey));
                    _embeddingClient = _azureOpenAIClient.GetEmbeddingClient("text-embedding-3-large");
                    _logger?.LogInformation("🤖 Azure OpenAI embedding client initialized for Document Index");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "❌ Error initializing Azure OpenAI client for Document Index");
                }
            }
            else
            {
                _logger?.LogWarning("⚠️ Azure OpenAI credentials not found for Document Index - Vector search will not be available");
            }
        }

        /// <summary>
        /// Get configuration value with fallback to Values section (Azure Functions format)
        /// </summary>
        private string? GetConfigurationValue(string key, string? defaultValue = null)
        {
            // Try direct key first
            var value = _configuration?.GetValue<string>(key);

            // Try Values section if not found (Azure Functions format)
            if (string.IsNullOrEmpty(value))
            {
                value = _configuration?.GetValue<string>($"Values:{key}");
            }

            return !string.IsNullOrEmpty(value) ? value : defaultValue;
        }

        /// <summary>
        /// Check if the document index search service is available
        /// </summary>
        public bool IsAvailable => _indexClient != null;

        /// <summary>
        /// Upload document content to the document-index
        /// </summary>
        public async Task<DocumentIndexUploadResult> UploadDocumentAsync(DocumentIndexContent document)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new DocumentIndexUploadResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                if (document == null)
                {
                    return new DocumentIndexUploadResult
                    {
                        Success = false,
                        Error = "Document cannot be null"
                    };
                }

                _logger?.LogInformation("📄 Uploading document to index: {DocumentID}", document.Id);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                // Build complete content for vector search
                var contentForVector = BuildCompleteContent(document);

                // Generate embeddings for the complete content
                float[]? embeddings = await GenerateEmbeddingsAsync(contentForVector);
                
                if (embeddings != null)
                {
                    _logger?.LogInformation("✅ Generated {Dimensions} embeddings for document", embeddings.Length);
                }
                else
                {
                    _logger?.LogWarning("⚠️ Failed to generate embeddings for document - vector search will not be available for this document");
                }
                
                // Create search document from DocumentIndexContent
                var searchDocument = new Dictionary<string, object>
                {
                    ["id"] = document.Id ?? Guid.NewGuid().ToString(),
                    ["twinID"] = document.TwinID ?? "",
                    ["documentID"] = document.DocumentID ?? "",
                    ["customerID"] = document.CustomerID ?? "",
                    ["tituloDocumento"] = document.TituloDocumento ?? "",
                    ["documentName"] = document.DocumentName ?? "",
                    ["resumenEjecutivo"] = document.ResumenEjecutivo ?? "",
                    ["totalPages"] = document.TotalPages,
                    ["totalTokensInput"] = document.TotalTokensInput,
                    ["totalTokensOutput"] = document.TotalTokensOutput,
                    ["filePath"] = document.FilePath ?? "",
                    ["fileName"] = document.FileName ?? "",
                    ["processedAt"] = document.ProcessedAt,
                    ["contenido"] = document.Contenido ?? "",
                    ["titulo"] = document.Titulo ?? "",
                    ["pagina"] = document.Pagina,
                    ["textVector"] = embeddings ?? Array.Empty<float>(),
                    ["datosExtraidos"] = document.DatosExtraidos ?? new List<DatoExtraidoSearchable>()
                };

                // Upload document to search index
                var documents = new[] { new SearchDocument(searchDocument) };
                var uploadResult = await searchClient.MergeOrUploadDocumentsAsync(documents);

                var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();

                if (errors.Any())
                {
                    var errorMessages = errors.Select(e => e.ErrorMessage).ToList();
                    _logger?.LogError("❌ Error uploading document: {DocumentID} - Errors: {Errors}",
                        document.Id, string.Join(", ", errorMessages));

                    return new DocumentIndexUploadResult
                    {
                        Success = false,
                        Error = $"Error uploading document: {string.Join(", ", errorMessages)}"
                    };
                }

                _logger?.LogInformation("✅ Document uploaded successfully: {DocumentID}", document.Id);

                return new DocumentIndexUploadResult
                {
                    Success = true,
                    Message = $"Document '{document.DocumentName}' uploaded successfully",
                    IndexName = IndexName,
                    DocumentId = document.Id
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Error uploading document: {DocumentID}", document?.Id);
                return new DocumentIndexUploadResult
                {
                    Success = false,
                    Error = $"Error uploading document: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Build complete content for vector search by combining all relevant fields
        /// </summary>
        private static string BuildCompleteContent(DocumentIndexContent document)
        {
            var content = new List<string>();

            if (!string.IsNullOrEmpty(document.TituloDocumento))
                content.Add($"Título: {document.TituloDocumento}");

            if (!string.IsNullOrEmpty(document.DocumentName))
                content.Add($"Nombre: {document.DocumentName}");

            if (!string.IsNullOrEmpty(document.ResumenEjecutivo))
                content.Add($"Resumen Ejecutivo: {document.ResumenEjecutivo}");

            if (!string.IsNullOrEmpty(document.Titulo))
                content.Add($"Sección: {document.Titulo}");

            if (!string.IsNullOrEmpty(document.Contenido))
                content.Add($"Contenido: {document.Contenido}");

            if (document.Pagina > 0)
                content.Add($"Página: {document.Pagina}");

            if (document.TotalPages > 0)
                content.Add($"Total Páginas: {document.TotalPages}");

            return string.Join(". ", content);
        }

        /// <summary>
        /// Upload multiple documents to the document-index
        /// </summary>
        public async Task<DocumentIndexUploadResult> UploadMultipleDocumentsAsync(List<DocumentIndexContent> documents)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new DocumentIndexUploadResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                if (documents == null || documents.Count == 0)
                {
                    return new DocumentIndexUploadResult
                    {
                        Success = false,
                        Error = "Documents list cannot be null or empty"
                    };
                }

                _logger?.LogInformation("📄 Uploading {DocumentCount} documents to index", documents.Count);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                // Create search documents from DocumentIndexContent list
                var searchDocuments = new List<SearchDocument>();

                foreach (var document in documents)
                {
                    // Generate embeddings for each document if not already provided
                    float[]? embeddings = document.TextVector;
                    
                    if (embeddings == null || embeddings.Length == 0)
                    {
                        var contentForVector = BuildCompleteContent(document);
                        embeddings = await GenerateEmbeddingsAsync(contentForVector);
                        
                        if (embeddings != null)
                        {
                            _logger?.LogInformation("✅ Generated embeddings for document: {DocumentID}", document.DocumentID);
                        }
                        else
                        {
                            _logger?.LogWarning("⚠️ Failed to generate embeddings for document: {DocumentID}", document.DocumentID);
                        }
                    }
                    
                    var searchDocument = new Dictionary<string, object>
                    {
                        ["id"] = document.Id ?? Guid.NewGuid().ToString(),
                        ["twinID"] = document.TwinID ?? "",
                        ["documentID"] = document.DocumentID ?? "",
                        ["customerID"] = document.CustomerID ?? "",
                        ["tituloDocumento"] = document.TituloDocumento ?? "",
                        ["documentName"] = document.DocumentName ?? "",
                        ["resumenEjecutivo"] = document.ResumenEjecutivo ?? "",
                        ["totalPages"] = document.TotalPages,
                        ["totalTokensInput"] = document.TotalTokensInput,
                        ["totalTokensOutput"] = document.TotalTokensOutput,
                        ["filePath"] = document.FilePath ?? "",
                        ["fileName"] = document.FileName ?? "",
                        ["processedAt"] = document.ProcessedAt,
                        ["contenido"] = document.Contenido ?? "",
                        ["titulo"] = document.Titulo ?? "",
                        ["pagina"] = document.Pagina,
                        ["textVector"] = embeddings ?? Array.Empty<float>(),
                        ["datosExtraidos"] = document.DatosExtraidos ?? new List<DatoExtraidoSearchable>()
                    };

                    searchDocuments.Add(new SearchDocument(searchDocument));
                }

                // Upload documents to search index
                var uploadResult = await searchClient.MergeOrUploadDocumentsAsync(searchDocuments);

                var successCount = uploadResult.Value.Results.Count(r => r.Succeeded);
                var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();

                if (errors.Any())
                {
                    var errorMessages = errors.Select(e => $"{e.Key}: {e.ErrorMessage}").ToList();
                    _logger?.LogWarning("⚠️ Partial upload completed: {SuccessCount}/{TotalCount} documents uploaded. Errors: {Errors}",
                        successCount, documents.Count, string.Join(", ", errorMessages));

                    return new DocumentIndexUploadResult
                    {
                        Success = false,
                        Error = $"Partial upload: {successCount}/{documents.Count} documents uploaded",
                        DocumentsUploaded = successCount,
                        TotalDocuments = documents.Count
                    };
                }

                _logger?.LogInformation("✅ All {DocumentCount} documents uploaded successfully", documents.Count);

                return new DocumentIndexUploadResult
                {
                    Success = true,
                    Message = $"All {documents.Count} documents uploaded successfully",
                    IndexName = IndexName,
                    DocumentsUploaded = successCount,
                    TotalDocuments = documents.Count
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Error uploading multiple documents");
                return new DocumentIndexUploadResult
                {
                    Success = false,
                    Error = $"Error uploading documents: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Creates the document index with SearchIndex configuration for Azure Cognitive Search
        /// </summary>
        public async Task<DocumentIndexUploadResult> CreateDocumentSearchIndexAsync()
        {
            try
            {
                if (!IsAvailable)
                {
                    return new DocumentIndexUploadResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger?.LogInformation("📄 Creating Document Search Index: {IndexName}", IndexName);

                // Define search fields based on the DocumentIndexContent class structure
                var fields = new List<SearchField>
                {
                    // Primary identification field
                    new SimpleField("id", SearchFieldDataType.String)
                    {
                        IsKey = true,
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // Twin ID for filtering and faceting
                    new SearchableField("twinID")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true
                    },

                    // Document identification
                    new SearchableField("documentID")
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // Customer ID for multi-tenant support
                    new SearchableField("customerID")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },

                    // Document title
                    new SearchableField("tituloDocumento")
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // Document name
                    new SearchableField("documentName")
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // Executive summary for semantic search
                    new SearchableField("resumenEjecutivo")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // Total pages - for filtering
                    new SimpleField("totalPages", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // Token counts for analytics
                    new SimpleField("totalTokensInput", SearchFieldDataType.Double)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    new SimpleField("totalTokensOutput", SearchFieldDataType.Double)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // File path and name for document tracking
                    new SearchableField("filePath")
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    new SearchableField("fileName")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true
                    },

                    // Processing timestamp
                    new SimpleField("processedAt", SearchFieldDataType.DateTimeOffset)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    },

                    // Main content for full-text search
                    new SearchableField("contenido")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // Section title for structured search
                    new SearchableField("titulo")
                    {
                        IsFilterable = true,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // Page number for filtering
                    new SimpleField("pagina", SearchFieldDataType.Int32)
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // Vector field for semantic similarity search
                    new SearchField("textVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                    {
                        IsSearchable = true,
                        VectorSearchDimensions = 1536,
                        VectorSearchProfileName = VectorSearchProfile
                    },

                    // Complex collection for extracted data (names, addresses, contracts, etc.)
                    new SearchField("datosExtraidos", SearchFieldDataType.Collection(SearchFieldDataType.Complex))
                    {
                        Fields =
                        {
                            new SearchableField("nombrePropiedad") 
                            { 
                                IsFilterable = true, 
                                IsFacetable = true
                                // IsSortable removed - cannot sort on fields in collections
                            },
                            new SearchableField("valorPropiedad") 
                            { 
                                IsFilterable = true,
                                AnalyzerName = LexicalAnalyzerName.EsLucene // Spanish text analyzer
                            },
                            new SearchableField("contexto") 
                            { 
                                AnalyzerName = LexicalAnalyzerName.EsLucene
                            }
                        }
                    }
                };

                // Configure vector search
                var vectorSearch = new VectorSearch();

                // Add HNSW algorithm configuration
                vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration(HnswAlgorithmConfig));

                // Add vector search profile
                vectorSearch.Profiles.Add(new VectorSearchProfile(VectorSearchProfile, HnswAlgorithmConfig));

                // Configure semantic search
                var semanticSearch = new SemanticSearch();
                var prioritizedFields = new SemanticPrioritizedFields
                {
                    TitleField = new SemanticField("tituloDocumento")
                };

                // Content fields for semantic ranking
                prioritizedFields.ContentFields.Add(new SemanticField("resumenEjecutivo"));
                prioritizedFields.ContentFields.Add(new SemanticField("contenido"));
                prioritizedFields.ContentFields.Add(new SemanticField("titulo"));

                // Keywords fields for semantic ranking
                prioritizedFields.KeywordsFields.Add(new SemanticField("documentID"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("twinID"));
                prioritizedFields.KeywordsFields.Add(new SemanticField("fileName"));

                semanticSearch.Configurations.Add(new SemanticConfiguration("document-semantic-config", prioritizedFields));

                // Create the document search index
                var index = new SearchIndex(IndexName, fields)
                {
                    VectorSearch = vectorSearch,
                    SemanticSearch = semanticSearch
                };

                var result = await _indexClient!.CreateOrUpdateIndexAsync(index);
                _logger?.LogInformation("✅ Document Index '{IndexName}' created successfully", IndexName);

                return new DocumentIndexUploadResult
                {
                    Success = true,
                    Message = $"Document Index '{IndexName}' created successfully",
                    IndexName = IndexName,
                    DocumentsUploaded = fields.Count
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Error creating Document Index");
                return new DocumentIndexUploadResult
                {
                    Success = false,
                    Error = $"Error creating Document Index: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Creates the document index with SearchIndex configuration for Azure Cognitive Search
        /// </summary>
        public static SearchIndex CreateDocumentSearchIndex()
        {
            var searchFields = new List<SearchField>
            {
                new SearchField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                new SearchField("twinID", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true, IsFacetable = true },
                new SearchField("documentID", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true },
                new SearchField("customerID", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true },
                new SearchField("tituloDocumento", SearchFieldDataType.String) { IsSearchable = true },
                new SearchField("documentName", SearchFieldDataType.String) { IsSearchable = true },
                new SearchField("resumenEjecutivo", SearchFieldDataType.String) { IsSearchable = true },
                new SearchField("totalPages", SearchFieldDataType.Int32) { IsFilterable = true },
                new SearchField("totalTokensInput", SearchFieldDataType.Double) { IsFilterable = true },
                new SearchField("totalTokensOutput", SearchFieldDataType.Double) { IsFilterable = true },
                new SearchField("filePath", SearchFieldDataType.String) { IsSearchable = true },
                new SearchField("fileName", SearchFieldDataType.String) { IsSearchable = true },
                new SearchField("processedAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true },
                new SearchField("contenido", SearchFieldDataType.String) { IsSearchable = true },
                new SearchField("titulo", SearchFieldDataType.String) { IsSearchable = true },
                new SearchField("pagina", SearchFieldDataType.Int32) { IsFilterable = true },
                new SearchField("textVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    IsSearchable = true,
                    VectorSearchDimensions = 1536,
                    VectorSearchProfileName = "document-vector-profile"
                },
                new SearchField("datosExtraidos", SearchFieldDataType.Collection(SearchFieldDataType.Complex))
                {
                    IsSearchable = true,
                    Fields =
                    {
                        new SearchField("nombrePropiedad", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true, IsFacetable = true },
                        new SearchField("valorPropiedad", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true },
                        new SearchField("contexto", SearchFieldDataType.String) { IsSearchable = true }
                    }
                }
            };

            var index = new SearchIndex("document-index", searchFields)
            {
                VectorSearch = new VectorSearch()
                {
                    Algorithms =
                    {
                        new HnswAlgorithmConfiguration("document-hnsw-config")
                    },
                    Profiles =
                    {
                        new VectorSearchProfile("document-vector-profile", "document-hnsw-config")
                    }
                }
            };

            return index;
        }

        /// <summary>
        /// Search documents by extracted data property name and value
        /// </summary>
        /// <param name="propertyName">Name of the extracted property (e.g., "NombrePropietario", "NumeroContrato")</param>
        /// <param name="propertyValue">Value to search for</param>
        /// <param name="twinId">Optional: Filter by TwinID</param>
        /// <param name="documentId">Optional: Filter by DocumentID</param>
        /// <returns>List of matching documents</returns>
        public async Task<DocumentSearchResult> SearchByExtractedDataAsync(
            string propertyName, 
            string? propertyValue = null, 
            string? twinId = null,
            string? documentId = null)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new DocumentSearchResult
                    {
                        Success = false,
                        Error = "Azure Search service not available",
                        Documents = new List<DocumentIndexContent>()
                    };
                }

                _logger?.LogInformation("🔍 Searching documents by extracted data: Property={PropertyName}, Value={PropertyValue}", 
                    propertyName, propertyValue);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                // Build filter for extracted data
                var filters = new List<string>();

                // Filter by property name (always required)
                if (!string.IsNullOrEmpty(propertyValue))
                {
                    filters.Add($"datosExtraidos/any(d: d/nombrePropiedad eq '{propertyName}' and search.in(d/valorPropiedad, '{propertyValue}', '|'))");
                }
                else
                {
                    // Search for any document that has this property name
                    filters.Add($"datosExtraidos/any(d: d/nombrePropiedad eq '{propertyName}')");
                }

                // Add optional filters
                if (!string.IsNullOrEmpty(twinId))
                {
                    filters.Add($"twinID eq '{twinId}'");
                }

                if (!string.IsNullOrEmpty(documentId))
                {
                    filters.Add($"documentID eq '{documentId}'");
                }

                var searchOptions = new SearchOptions
                {
                    Filter = string.Join(" and ", filters),
                    Size = 1000,
                    IncludeTotalCount = true
                };

                var results = await searchClient.SearchAsync<DocumentIndexContent>("*", searchOptions);

                var documents = new List<DocumentIndexContent>();
                long totalCount = results.Value.TotalCount ?? 0;

                await foreach (var result in results.Value.GetResultsAsync())
                {
                    documents.Add(result.Document);
                }

                _logger?.LogInformation("✅ Found {Count} documents with extracted data property {PropertyName}", 
                    documents.Count, propertyName);

                return new DocumentSearchResult
                {
                    Success = true,
                    Documents = documents,
                    TotalCount = totalCount,
                    Message = $"Found {documents.Count} documents"
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Error searching by extracted data");
                return new DocumentSearchResult
                {
                    Success = false,
                    Error = $"Error searching documents: {ex.Message}",
                    Documents = new List<DocumentIndexContent>()
                };
            }
        }

        /// <summary>
        /// Get all unique property names extracted across documents
        /// </summary>
        /// <param name="twinId">Optional: Filter by TwinID</param>
        /// <returns>List of unique property names with their counts</returns>
        public async Task<ExtractedDataFacetsResult> GetExtractedDataFacetsAsync(string? twinId = null)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new ExtractedDataFacetsResult
                    {
                        Success = false,
                        Error = "Azure Search service not available",
                        PropertyNames = new Dictionary<string, long>()
                    };
                }

                _logger?.LogInformation("🔍 Getting facets for extracted data properties");

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                var searchOptions = new SearchOptions
                {
                    Facets = { "datosExtraidos/nombrePropiedad,count:100" },
                    Size = 0 // We only want facets, not documents
                };

                if (!string.IsNullOrEmpty(twinId))
                {
                    searchOptions.Filter = $"twinID eq '{twinId}'";
                }

                var results = await searchClient.SearchAsync<DocumentIndexContent>("*", searchOptions);

                var propertyNames = new Dictionary<string, long>();

                if (results.Value.Facets.TryGetValue("datosExtraidos/nombrePropiedad", out var facetResults))
                {
                    foreach (var facet in facetResults)
                    {
                        if (facet.Value != null && facet.Count.HasValue)
                        {
                            propertyNames[facet.Value.ToString()!] = facet.Count.Value;
                        }
                    }
                }

                _logger?.LogInformation("✅ Found {Count} unique extracted data property types", propertyNames.Count);

                return new ExtractedDataFacetsResult
                {
                    Success = true,
                    PropertyNames = propertyNames,
                    Message = $"Found {propertyNames.Count} unique property types"
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Error getting extracted data facets");
                return new ExtractedDataFacetsResult
                {
                    Success = false,
                    Error = $"Error getting facets: {ex.Message}",
                    PropertyNames = new Dictionary<string, long>()
                };
            }
        }

        /// <summary>
        /// Generate embeddings for text using Azure OpenAI
        /// </summary>
        /// <param name="text">Text to generate embeddings for</param>
        /// <returns>Float array of embeddings or null if generation fails</returns>
        private async Task<float[]?> GenerateEmbeddingsAsync(string text)
        {
            try
            {
                if (_embeddingClient == null)
                {
                    _logger?.LogWarning("⚠️ Embedding client not available");
                    return null;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger?.LogWarning("⚠️ Text content is empty");
                    return null;
                }

                // Truncate text if too long (Azure OpenAI has token limits)
                if (text.Length > 8000)
                {
                    text = text.Substring(0, 8000);
                    _logger?.LogInformation("📏 Text truncated to 8000 characters for embedding generation");
                }

                var embeddingOptions = new OpenAI.Embeddings.EmbeddingGenerationOptions
                {
                    Dimensions = EmbeddingDimensions
                };

                var embedding = await _embeddingClient.GenerateEmbeddingAsync(text, embeddingOptions);
                var embeddings = embedding.Value.ToFloats().ToArray();

                _logger?.LogInformation("✅ Generated embedding vector with {Dimensions} dimensions", embeddings.Length);
                return embeddings;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "⚠️ Failed to generate embeddings");
                return null;
            }
        }

        /// <summary>
        /// Perform semantic/vector search on documents using natural language query
        /// Searches through document content semantically to find "PÁGINA 3" or similar content
        /// </summary>
        /// <param name="query">Natural language search query (e.g., "Este es un modelo básico de contrato")</param>
        /// <param name="twinId">Optional: Filter by TwinID</param>
        /// <param name="documentId">Optional: Filter by DocumentID</param>
        /// <param name="topResults">Number of top results to return (default: 10)</param>
        /// <param name="useHybridSearch">Use hybrid search (combines vector + full-text)</param>
        /// <returns>Search results with semantic relevance scores</returns>
        public async Task<DocumentSemanticSearchResult> SemanticSearchAsync(
            string query,
            string? twinId = null,
            string? documentId = null,
            int topResults = 10,
            bool useHybridSearch = true)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new DocumentSemanticSearchResult
                    {
                        Success = false,
                        Error = "Azure Search service not available",
                        Documents = new List<DocumentSearchResultItem>()
                    };
                }

                if (string.IsNullOrWhiteSpace(query))
                {
                    return new DocumentSemanticSearchResult
                    {
                        Success = false,
                        Error = "Search query cannot be empty",
                        Documents = new List<DocumentSearchResultItem>()
                    };
                }

                _logger?.LogInformation("🔍 Performing semantic search: Query='{Query}', TwinID={TwinID}, TopResults={TopResults}",
                    query, twinId, topResults);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                // Generate embeddings for the query
                float[]? queryEmbeddings = await GenerateEmbeddingsAsync(query);

                if (queryEmbeddings == null && !useHybridSearch)
                {
                    _logger?.LogWarning("⚠️ Failed to generate query embeddings and hybrid search is disabled");
                    return new DocumentSemanticSearchResult
                    {
                        Success = false,
                        Error = "Failed to generate embeddings for vector search",
                        Documents = new List<DocumentSearchResultItem>()
                    };
                }

                // Build search options
                var searchOptions = new SearchOptions
                {
                    Size = topResults,
                    IncludeTotalCount = true,
                    QueryType = SearchQueryType.Semantic,
                    SemanticSearch = new()
                    {
                        SemanticConfigurationName = "document-semantic-config",
                        QueryCaption = new(QueryCaptionType.Extractive),
                        QueryAnswer = new(QueryAnswerType.Extractive)
                    }
                };

                // Add vector search if embeddings are available
                if (queryEmbeddings != null)
                {
                    searchOptions.VectorSearch = new()
                    {
                        Queries = { new VectorizedQuery(queryEmbeddings.ToArray()) { KNearestNeighborsCount = topResults, Fields = { "textVector" } } }
                    };
                }

                // Add filters if provided
                var filters = new List<string>();
                if (!string.IsNullOrEmpty(twinId))
                {
                    filters.Add($"twinID eq '{twinId}'");
                }
                if (!string.IsNullOrEmpty(documentId))
                {
                    filters.Add($"documentID eq '{documentId}'");
                }
                if (filters.Any())
                {
                    searchOptions.Filter = string.Join(" and ", filters);
                }

                // Select fields to return
                searchOptions.Select.Add("id");
                searchOptions.Select.Add("twinID");
                searchOptions.Select.Add("documentID");
                searchOptions.Select.Add("customerID");
                searchOptions.Select.Add("tituloDocumento");
                searchOptions.Select.Add("documentName");
                searchOptions.Select.Add("resumenEjecutivo");
                searchOptions.Select.Add("titulo");
                searchOptions.Select.Add("contenido");
                searchOptions.Select.Add("pagina");
                searchOptions.Select.Add("fileName");
                searchOptions.Select.Add("processedAt");

                // Perform the search
                Azure.Response<SearchResults<DocumentIndexContent>> response;
                if (useHybridSearch)
                {
                    // Hybrid search: combines vector search with full-text search
                    response = await searchClient.SearchAsync<DocumentIndexContent>(query, searchOptions);
                }
                else
                {
                    // Pure vector search
                    response = await searchClient.SearchAsync<DocumentIndexContent>("*", searchOptions);
                }

                var results = new List<DocumentSearchResultItem>();
                long totalCount = response.Value.TotalCount ?? 0;

                await foreach (var result in response.Value.GetResultsAsync())
                {
                    var searchResult = new DocumentSearchResultItem
                    {
                        Document = result.Document,
                        Score = result.Score ?? 0.0,
                        RerankerScore = result.SemanticSearch?.RerankerScore
                    };

                    // Add captions if available
                    if (result.SemanticSearch?.Captions != null)
                    {
                        searchResult.Captions = result.SemanticSearch.Captions
                            .Select(c => new SearchCaption
                            {
                                Text = c.Text,
                                Highlights = c.Highlights
                            }).ToList();
                    }

                    results.Add(searchResult);
                }

                _logger?.LogInformation("✅ Semantic search completed: Found {Count} results out of {TotalCount} total",
                    results.Count, totalCount);

                return new DocumentSemanticSearchResult
                {
                    Success = true,
                    Documents = results,
                    TotalCount = totalCount,
                    Query = query,
                    Message = $"Found {results.Count} semantically relevant documents",
                    UsedVectorSearch = queryEmbeddings != null,
                    UsedHybridSearch = useHybridSearch && queryEmbeddings != null
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Error performing semantic search");
                return new DocumentSemanticSearchResult
                {
                    Success = false,
                    Error = $"Error performing semantic search: {ex.Message}",
                    Documents = new List<DocumentSearchResultItem>()
                };
            }
        }

        /// <summary>
        /// Search documents by page content (e.g., "PÁGINA 3")
        /// Combines semantic search with page number filtering
        /// </summary>
        /// <param name="pageContent">Content to search for (e.g., "Este es un modelo básico de contrato")</param>
        /// <param name="pageNumber">Optional: Specific page number to filter</param>
        /// <param name="twinId">Optional: Filter by TwinID</param>
        /// <param name="topResults">Number of results to return</param>
        /// <returns>Search results ordered by relevance</returns>
        public async Task<DocumentSemanticSearchResult> SearchByPageContentAsync(
            string pageContent,
            int? pageNumber = null,
            string? twinId = null,
            int topResults = 10)
        {
            try
            {
                _logger?.LogInformation("📄 Searching by page content: Content='{Content}', Page={Page}",
                    pageContent.Length > 50 ? pageContent.Substring(0, 50) + "..." : pageContent, pageNumber);

                // Build filter for page number if provided
                string? additionalFilter = null;
                if (pageNumber.HasValue)
                {
                    additionalFilter = $"pagina eq {pageNumber.Value}";
                    
                    // Combine with twinId filter if present
                    if (!string.IsNullOrEmpty(twinId))
                    {
                        additionalFilter += $" and twinID eq '{twinId}'";
                    }
                }

                // Perform semantic search with filters
                var result = await SemanticSearchAsync(
                    query: pageContent,
                    twinId: string.IsNullOrEmpty(additionalFilter) ? twinId : null,
                    topResults: topResults,
                    useHybridSearch: true);

                // Apply page filter manually if needed (when combined with other filters)
                if (pageNumber.HasValue && result.Success)
                {
                    result.Documents = result.Documents
                        .Where(d => d.Document?.Pagina == pageNumber.Value)
                        .ToList();
                    result.Message = $"Found {result.Documents.Count} results on page {pageNumber.Value}";
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Error searching by page content");
                return new DocumentSemanticSearchResult
                {
                    Success = false,
                    Error = $"Error searching by page content: {ex.Message}",
                    Documents = new List<DocumentSearchResultItem>()
                };
            }
        }

        /// <summary>
        /// Get unique documents by documentID for a given TwinID and optional CustomerID
        /// Returns only the first document (summary) for each unique documentID, eliminating duplicates from different pages
        /// Perfect for showing a list of distinct documents without page-by-page details
        /// </summary>
        /// <param name="twinId">TwinID to filter documents (required)</param>
        /// <param name="customerID">Optional: CustomerID to filter documents</param>
        /// <param name="topResults">Maximum number of unique documents to return (default: 50)</param>
        /// <returns>List of unique documents with summary information only</returns>
        public async Task<DocumentSearchResult> GetUniqueDocumentsByTwinIdAsync(
            string twinId,
            string? customerID = null,
            int topResults = 50)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new DocumentSearchResult
                    {
                        Success = false,
                        Error = "Azure Search service not available",
                        Documents = new List<DocumentIndexContent>()
                    };
                }

                if (string.IsNullOrWhiteSpace(twinId))
                {
                    return new DocumentSearchResult
                    {
                        Success = false,
                        Error = "TwinID cannot be empty",
                        Documents = new List<DocumentIndexContent>()
                    };
                }

                _logger?.LogInformation("📚 Getting unique documents for TwinID: {TwinID}, CustomerID: {CustomerID}", 
                    twinId, customerID ?? "ALL");

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                // Build filter with both TwinID and optional CustomerID
                var filters = new List<string> { $"twinID eq '{twinId}'" };
                if (!string.IsNullOrEmpty(customerID))
                {
                    filters.Add($"customerID eq '{customerID}'");
                }

                // Search with filter and ordering to get the first occurrence of each documentID
                var searchOptions = new SearchOptions
                {
                    Filter = string.Join(" and ", filters),
                    OrderBy = { "documentID", "pagina" },
                    Size = 1000,
                    IncludeTotalCount = true
                };

                // Select only summary fields (not page content)
                searchOptions.Select.Add("id");
                searchOptions.Select.Add("twinID");
                searchOptions.Select.Add("documentID");
                searchOptions.Select.Add("customerID");
                searchOptions.Select.Add("tituloDocumento");
                searchOptions.Select.Add("documentName");
                searchOptions.Select.Add("resumenEjecutivo");
                searchOptions.Select.Add("totalPages");
                searchOptions.Select.Add("totalTokensInput");
                searchOptions.Select.Add("totalTokensOutput");
                searchOptions.Select.Add("filePath");
                searchOptions.Select.Add("fileName");
                searchOptions.Select.Add("processedAt");

                var results = await searchClient.SearchAsync<DocumentIndexContent>("*", searchOptions);

                // Use Dictionary to track unique documentIDs and keep only the first occurrence
                var uniqueDocuments = new Dictionary<string, DocumentIndexContent>();
                long totalCount = results.Value.TotalCount ?? 0;

                await foreach (var result in results.Value.GetResultsAsync())
                {
                    var doc = result.Document;
                    
                    // Only add if we haven't seen this documentID yet
                    if (!string.IsNullOrEmpty(doc.DocumentID) && !uniqueDocuments.ContainsKey(doc.DocumentID))
                    {
                        uniqueDocuments[doc.DocumentID] = doc;
                        
                        // Stop when we have enough unique documents
                        if (uniqueDocuments.Count >= topResults)
                        {
                            break;
                        }
                    }
                }

                // Generate SAS URLs for each unique document
                if (_configuration != null && uniqueDocuments.Count > 0)
                {
                    try
                    {
                        _logger?.LogInformation("🔗 Generating SAS URLs for {Count} unique documents", uniqueDocuments.Count);
                        
                        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                        var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                        var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                        foreach (var doc in uniqueDocuments.Values)
                        {
                            if (!string.IsNullOrEmpty(doc.FilePath) && !string.IsNullOrEmpty(doc.FileName))
                            {
                                try
                                {
                                    var fullFilePath = $"{doc.FilePath}/{doc.FileName}";
                                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(24));
                                    
                                    if (!string.IsNullOrEmpty(sasUrl))
                                    {
                                        doc.URL = sasUrl;
                                        _logger?.LogInformation("✅ Generated SAS URL for document: {DocumentName}", doc.DocumentName);
                                    }
                                }
                                catch (Exception sasEx)
                                {
                                    _logger?.LogWarning(sasEx, "⚠️ Failed to generate SAS URL for document: {DocumentName}", doc.DocumentName);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "⚠️ Error initializing DataLake client for SAS URL generation");
                    }
                }

                var documentList = uniqueDocuments.Values.ToList();
                var filterDesc = !string.IsNullOrEmpty(customerID) 
                    ? $"Twin '{twinId}' and Customer '{customerID}'" 
                    : $"Twin '{twinId}'";

                _logger?.LogInformation("✅ Found {UniqueCount} unique documents out of {TotalCount} total entries for {Filter}",
                    documentList.Count, totalCount, filterDesc);

                return new DocumentSearchResult
                {
                    Success = true,
                    Documents = documentList,
                    TotalCount = documentList.Count,
                    Message = $"Found {documentList.Count} unique documents for {filterDesc}"
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Error getting unique documents for TwinID: {TwinID}, CustomerID: {CustomerID}", 
                    twinId, customerID);
                return new DocumentSearchResult
                {
                    Success = false,
                    Error = $"Error getting unique documents: {ex.Message}",
                    Documents = new List<DocumentIndexContent>()
                };
            }
        }

        /// <summary>
        /// Search unique documents with optional filtering
        /// Returns only one document per documentID with summary information
        /// </summary>
        /// <param name="searchQuery">Optional search query (use "*" for all documents)</param>
        /// <param name="twinId">Optional: Filter by TwinID</param>
        /// <param name="customerID">Optional: Filter by CustomerID</param>
        /// <param name="topResults">Maximum number of unique documents to return</param>
        /// <returns>List of unique documents with summary data only</returns>
        public async Task<DocumentSearchResult> SearchUniqueDocumentsAsync(
            string searchQuery = "*",
            string? twinId = null,
            string? customerID = null,
            int topResults = 50)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new DocumentSearchResult
                    {
                        Success = false,
                        Error = "Azure Search service not available",
                        Documents = new List<DocumentIndexContent>()
                    };
                }

                _logger?.LogInformation("🔍 Searching unique documents: Query='{Query}', TwinID={TwinID}, CustomerID={CustomerID}",
                    searchQuery, twinId, customerID);

                var searchClient = new SearchClient(new Uri(_searchEndpoint!), IndexName, new AzureKeyCredential(_searchApiKey!));

                // Build filters
                var filters = new List<string>();
                if (!string.IsNullOrEmpty(twinId))
                {
                    filters.Add($"twinID eq '{twinId}'");
                }
                if (!string.IsNullOrEmpty(customerID))
                {
                    filters.Add($"customerID eq '{customerID}'");
                }

                var searchOptions = new SearchOptions
                {
                    OrderBy = { "documentID", "pagina" }, // Get first page of each document
                    Size = 1000,
                    IncludeTotalCount = true
                };

                if (filters.Any())
                {
                    searchOptions.Filter = string.Join(" and ", filters);
                }

                // Select only summary fields (exclude page-specific content)
                searchOptions.Select.Add("id");
                searchOptions.Select.Add("twinID");
                searchOptions.Select.Add("documentID");
                searchOptions.Select.Add("customerID");
                searchOptions.Select.Add("tituloDocumento");
                searchOptions.Select.Add("documentName");
                searchOptions.Select.Add("resumenEjecutivo");
                searchOptions.Select.Add("totalPages");
                searchOptions.Select.Add("totalTokensInput");
                searchOptions.Select.Add("totalTokensOutput");
                searchOptions.Select.Add("filePath");
                searchOptions.Select.Add("fileName");
                searchOptions.Select.Add("processedAt");

                // Add search fields if not wildcard
                if (searchQuery != "*")
                {
                    searchOptions.SearchFields.Add("tituloDocumento");
                    searchOptions.SearchFields.Add("documentName");
                    searchOptions.SearchFields.Add("resumenEjecutivo");
                }

                var results = await searchClient.SearchAsync<DocumentIndexContent>(searchQuery, searchOptions);

                // Deduplicate by documentID
                var uniqueDocuments = new Dictionary<string, DocumentIndexContent>();
                long totalCount = results.Value.TotalCount ?? 0;

                await foreach (var result in results.Value.GetResultsAsync())
                {
                    var doc = result.Document;
                    
                    if (!string.IsNullOrEmpty(doc.DocumentID) && !uniqueDocuments.ContainsKey(doc.DocumentID))
                    {
                        uniqueDocuments[doc.DocumentID] = doc;
                        
                        if (uniqueDocuments.Count >= topResults)
                        {
                            break;
                        }
                    }
                }

                var documentList = uniqueDocuments.Values
                    .OrderByDescending(d => d.ProcessedAt) // Most recent first
                    .ToList();

                _logger?.LogInformation("✅ Found {UniqueCount} unique documents out of {TotalCount} total entries",
                    documentList.Count, totalCount);

                return new DocumentSearchResult
                {
                    Success = true,
                    Documents = documentList,
                    TotalCount = documentList.Count,
                    Message = $"Found {documentList.Count} unique documents"
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Error searching unique documents");
                return new DocumentSearchResult
                {
                    Success = false,
                    Error = $"Error searching unique documents: {ex.Message}",
                    Documents = new List<DocumentIndexContent>()
                };
            }
        }
    }

    /// <summary>
    /// Document index content model with all searchable fields
    /// </summary>
    public class DocumentIndexContent
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string? Id { get; set; }

        public string? URL { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("twinID")]
        public string? TwinID { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("documentID")]
        public string? DocumentID { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("customerID")]
        public string? CustomerID { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("tituloDocumento")]
        public string? TituloDocumento { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("documentName")]
        public string? DocumentName { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("resumenEjecutivo")]
        public string? ResumenEjecutivo { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("totalPages")]
        public int TotalPages { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("totalTokensInput")]
        public double TotalTokensInput { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("totalTokensOutput")]
        public double TotalTokensOutput { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("filePath")]
        public string? FilePath { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("fileName")]
        public string? FileName { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("processedAt")]
        public DateTime ProcessedAt { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("contenido")]
        public string? Contenido { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("titulo")]
        public string? Titulo { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("pagina")]
        public int Pagina { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("textVector")]
        public float[]? TextVector { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("datosExtraidos")]
        public List<DatoExtraidoSearchable>? DatosExtraidos { get; set; }
    }

    /// <summary>
    /// Searchable extracted data model (for Azure Cognitive Search complex type)
    /// </summary>
    public class DatoExtraidoSearchable
    {
        [System.Text.Json.Serialization.JsonPropertyName("nombrePropiedad")]
        public string NombrePropiedad { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("valorPropiedad")]
        public string ValorPropiedad { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("contexto")]
        public string Contexto { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result class for document index upload operations
    /// </summary>
    public class DocumentIndexUploadResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public string? IndexName { get; set; }
        public string? DocumentId { get; set; }
        public int DocumentsUploaded { get; set; }
        public int TotalDocuments { get; set; }
    }

    /// <summary>
    /// Result class for document search operations
    /// </summary>
    public class DocumentSearchResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public List<DocumentIndexContent> Documents { get; set; } = new();
        public long TotalCount { get; set; }
    }

    /// <summary>
    /// Result class for extracted data facets (property name counts)
    /// </summary>
    public class ExtractedDataFacetsResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public Dictionary<string, long> PropertyNames { get; set; } = new();
    }

    /// <summary>
    /// Result class for semantic/vector search operations
    /// </summary>
    public class DocumentSemanticSearchResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public string? Query { get; set; }
        public List<DocumentSearchResultItem> Documents { get; set; } = new();
        public long TotalCount { get; set; }
        public bool UsedVectorSearch { get; set; }
        public bool UsedHybridSearch { get; set; }
    }

    /// <summary>
    /// Individual search result item with score and captions
    /// </summary>
    public class DocumentSearchResultItem
    {
        public DocumentIndexContent? Document { get; set; }
        public double Score { get; set; }
        public double? RerankerScore { get; set; }
        public List<SearchCaption> Captions { get; set; } = new();
    }

    /// <summary>
    /// Search caption/highlight from semantic search
    /// </summary>
    public class SearchCaption
    {
        public string Text { get; set; } = string.Empty;
        public string? Highlights { get; set; }
    }
}
