using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.AI.OpenAI;
using OpenAI.Embeddings;
using TwinAgentsNetwork.Models;
using TwinAgentsLibrary.Models;
using TwinAgentsLibrary.Services;

namespace TwinAgentsNetwork.Services
{
    /// <summary>
    /// Servicio para crear y gestionar el índice Azure AI Search para análisis de fotos MiCasa
    /// Índice: micasafotosindex
    /// </summary>
    public class MiCasaFotosIndex
    {
        private readonly ILogger<MiCasaFotosIndex> _logger;
        private readonly IConfiguration _configuration;
        private readonly string? _searchEndpoint;
        private readonly string? _searchApiKey;
        private SearchIndexClient? _indexClient;
        private readonly AzureOpenAIClient? _azureOpenAIClient;
        private readonly EmbeddingClient? _embeddingClient;

        // Index configuration constants
        private const string IndexName = "micasafotosindex";
        private const int EmbeddingDimensions = 1536;
        private const string VectorSearchProfile = "myHnswProfile";
        private const string HnswAlgorithmConfig = "myHnsw";
        private const string SemanticConfig = "my-semantic-config";

        // Configuration keys
        private readonly string? _openAIEndpoint;
        private readonly string? _openAIApiKey;
        private readonly string? _embeddingDeployment;

        public bool IsAvailable => _indexClient != null;

        public MiCasaFotosIndex(ILogger<MiCasaFotosIndex> logger, IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            // Load Azure Search configuration with fallback to Values section (Azure Functions format)
            _searchEndpoint = GetConfigurationValue("AZURE_SEARCH_ENDPOINT");
            _searchApiKey = GetConfigurationValue("AZURE_SEARCH_API_KEY");

            // Load Azure OpenAI configuration
            _openAIEndpoint = GetConfigurationValue("AZURE_OPENAI_ENDPOINT") ?? GetConfigurationValue("AzureOpenAI:Endpoint");
            _openAIApiKey = GetConfigurationValue("AZURE_OPENAI_API_KEY") ?? GetConfigurationValue("AzureOpenAI:ApiKey");
            _embeddingDeployment = "text-embedding-3-large";

            // Initialize Azure Search client
            if (!string.IsNullOrEmpty(_searchEndpoint) && !string.IsNullOrEmpty(_searchApiKey))
            {
                try
                {
                    var credential = new AzureKeyCredential(_searchApiKey);
                    _indexClient = new SearchIndexClient(new Uri(_searchEndpoint), credential);
                    _logger.LogInformation("✅ MiCasa Fotos Index service initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error initializing Azure Search client for MiCasa Fotos Index");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Azure Search credentials not found - MiCasa Fotos Index service unavailable");
            }

            // Initialize Azure OpenAI client for embeddings
            if (!string.IsNullOrEmpty(_openAIEndpoint) && !string.IsNullOrEmpty(_openAIApiKey))
            {
                try
                {
                    _azureOpenAIClient = new AzureOpenAIClient(new Uri(_openAIEndpoint), new AzureKeyCredential(_openAIApiKey));
                    _embeddingClient = _azureOpenAIClient.GetEmbeddingClient(_embeddingDeployment);
                    _logger.LogInformation("🤖 Azure OpenAI embedding client initialized for MiCasa Fotos Index");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error initializing Azure OpenAI client for MiCasa Fotos Index");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Azure OpenAI credentials not found for MiCasa Fotos Index");
            }
        }

        /// <summary>
        /// Get configuration value with fallback to Values section (Azure Functions format)
        /// </summary>
        private string? GetConfigurationValue(string key, string? defaultValue = null)
        {
            // Try direct key first
            var value = _configuration.GetValue<string>(key);

            // Try Values section if not found (Azure Functions format)
            if (string.IsNullOrEmpty(value))
            {
                value = _configuration.GetValue<string>($"Values:{key}");
            }

            return !string.IsNullOrEmpty(value) ? value : defaultValue;
        }

        /// <summary>
        /// Create the micasafotosindex with vector and semantic search capabilities
        /// Stores all MiCasaPhotoAnalysisResult data with vector embeddings from AnalisisDetallado
        /// </summary>
        public async Task<MiCasaIndexResult> CreateMiCasaFotosIndexAsync()
        {
            try
            {
                if (!IsAvailable)
                {
                    return new MiCasaIndexResult
                    {
                        Success = false,
                        Error = "Azure Search service not available"
                    };
                }

                _logger.LogInformation("📷 Creating MiCasa Fotos Index: {IndexName}", IndexName);

                // Define search fields based on MiCasaPhotoAnalysisResult class schema
                var fields = new List<SearchField>
                {
                    // Primary identification field
                    new SimpleField("id", SearchFieldDataType.String)
                    {
                        IsKey = true,
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // TwinId field
                    new SearchableField("twinId")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true
                    },

                    // Tipo de sección field
                    new SearchableField("tipoSeccion")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },

                    // Nombre de sección field
                    new SearchableField("nombreSeccion")
                    {
                        IsFilterable = true,
                        IsSortable = true
                    },

                    // Piso field
                    new SearchableField("piso")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true
                    },

                    // Descripción genérica field
                    new SearchableField("descripcionGenerica")
                    {
                        IsFilterable = true
                    },

                    // Análisis detallado text field
                    new SearchableField("analisisDetallado")
                    {
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    },

                    // Vector field for semantic similarity search (1536 dimensions) from AnalisisDetallado
                    new SearchField("analisisDetalladoVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                    {
                        IsSearchable = true,
                        VectorSearchDimensions = EmbeddingDimensions,
                        VectorSearchProfileName = VectorSearchProfile
                    },

                    // Calidad general evaluation
                    new SearchableField("calidadGeneral")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    },

                    // Estado general field
                    new SearchableField("estadoGeneral")
                    {
                        IsFilterable = true
                    }

                    // Tipo de piso field
                    , new SearchableField("tipoPiso")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    }

                    // Calidad de piso field
                    , new SearchableField("calidadPiso")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    }

                    // Cortinas field
                    , new SearchableField("cortinas")
                    {
                        IsFilterable = true
                    }

                    // Muebles field
                    , new SearchableField("muebles")
                    {
                        IsFilterable = true
                    }

                    // Iluminación field
                    , new SearchableField("iluminacion")
                    {
                        IsFilterable = true
                    }

                    // Limpieza field
                    , new SearchableField("limpieza")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    }

                    // Habitabilidad field
                    , new SearchableField("habitabilidad")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    }

                    // HTML Full Description field
                    , new SearchableField("htmlFullDescription")
                    {
                        IsFilterable = false,
                        AnalyzerName = LexicalAnalyzerName.EsLucene
                    }

                    // ========== FILE INFORMATION FIELDS ==========

                    // Propiedad ID field
                    , new SearchableField("propiedadId")
                    {
                        IsFilterable = true,
                        IsSortable = true
                    }

                    // Casa ID field (for searching photos by house)
                    , new SearchableField("casaId")
                    {
                        IsFilterable = true,
                        IsFacetable = true,
                        IsSortable = true
                    }

                    // File name field
                    , new SearchableField("fileName")
                    {
                        IsFilterable = true,
                        IsSortable = true
                    }

                    // Container name field
                    , new SearchableField("containerName")
                    {
                        IsFilterable = true,
                        IsFacetable = true
                    }

                    // File path field
                    , new SearchableField("filePath")
                    {
                        IsFilterable = true,
                        IsSortable = true
                    }

                    // File size field (in bytes)
                    , new SimpleField("fileSize", SearchFieldDataType.Int64)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    }

                    // File uploaded timestamp
                    , new SimpleField("fileUploadedAt", SearchFieldDataType.DateTimeOffset)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    }

                    // Timestamp field
                    , new SimpleField("analyzedAt", SearchFieldDataType.DateTimeOffset)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    }

                    // ========== DIMENSIONES FIELDS ==========

                    // Dimensiones - Ancho field
                    , new SimpleField("dimensionesAncho", SearchFieldDataType.Double)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    }

                    // Dimensiones - Largo field
                    , new SimpleField("dimensionesLargo", SearchFieldDataType.Double)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    }

                    // Dimensiones - Alto field
                    , new SimpleField("dimensionesAlto", SearchFieldDataType.Double)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    }

                    // Dimensiones - Diámetro field
                    , new SimpleField("dimensionesDiametro", SearchFieldDataType.Double)
                    {
                        IsFilterable = true,
                        IsSortable = true,
                        IsFacetable = true
                    }
                };

                // Configure vector search (simplified version without vectorizer)
                var vectorSearch = new VectorSearch();

                // Add HNSW algorithm configuration
                vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration(HnswAlgorithmConfig));

                // Add vector search profile (manual embeddings only)
                vectorSearch.Profiles.Add(new VectorSearchProfile(VectorSearchProfile, HnswAlgorithmConfig));

                // Configure semantic search
                var semanticSearch = new SemanticSearch();
                var prioritizedFields = new SemanticPrioritizedFields
                {
                    TitleField = new SemanticField("nombreSeccion")
                };

                // Content fields for semantic ranking
                prioritizedFields.ContentFields.Add(new SemanticField("analisisDetallado"));
                prioritizedFields.ContentFields.Add(new SemanticField("descripcionGenerica"));

                semanticSearch.Configurations.Add(new SemanticConfiguration(SemanticConfig, prioritizedFields));

                // Create the MiCasa Fotos search index
                var index = new SearchIndex(IndexName, fields)
                {
                    VectorSearch = vectorSearch,
                    SemanticSearch = semanticSearch
                };

                var result = await _indexClient!.CreateOrUpdateIndexAsync(index);
                _logger.LogInformation("✅ MiCasa Fotos Index '{IndexName}' created successfully with {FieldCount} fields",
                    IndexName, fields.Count);

                return new MiCasaIndexResult
                {
                    Success = true,
                    Message = $"MiCasa Fotos Index '{IndexName}' created successfully",
                    IndexName = IndexName,
                    FieldsCount = fields.Count,
                    HasVectorSearch = true,
                    HasSemanticSearch = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating MiCasa Fotos Index");
                return new MiCasaIndexResult
                {
                    Success = false,
                    Error = $"Error creating MiCasa Fotos Index: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Inserta un resultado de análisis de foto MiCasa en el índice con embeddings vectoriales
        /// </summary>
        /// <param name="twinId">ID del Twin propietario de la foto</param>
        /// <param name="tipoSeccion">Tipo de sección (jardin, cocina, sala, etc.)</param>
        /// <param name="nombreSeccion">Nombre de la sección</param>
        /// <param name="analysis">Resultado del análisis generado por IA</param>
        /// <param name="propiedadId">ID de la propiedad (opcional)</param>
        /// <param name="casaId">ID de la casa (opcional)</param>
        /// <param name="fileName">Nombre del archivo de foto (opcional)</param>
        /// <param name="containerName">Nombre del contenedor en Azure Storage (opcional)</param>
        /// <param name="filePath">Ruta completa del archivo en Storage (opcional)</param>
        /// <param name="fileSize">Tamaño del archivo en bytes (opcional)</param>
        /// <param name="fileUploadedAt">Timestamp cuando se subió el archivo (opcional)</param>
        /// <param name="piso">Número o nombre del piso donde se encuentra la sección (opcional)</param>
        /// <returns>Resultado de la operación de inserción</returns>
        public async Task<MiCasaPhotoUploadResult> UploadPhotoAnalysisToIndexAsync(
            string twinId,
            string tipoSeccion,
            string nombreSeccion,
            MiCasaPhotoAnalysisResult analysis,
            string? propiedadId = null,
            string? casaId = null,
            string? fileName = null,
            string? containerName = null,
            string? filePath = null,
            long? fileSize = null,
            DateTimeOffset? fileUploadedAt = null,
            string? piso = null,
            double? dimensionesAncho = null,
            double? dimensionesLargo = null,
            double? dimensionesAlto = null,
            double? dimensionesDiametro = null)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new MiCasaPhotoUploadResult
                    {
                        Success = false,
                        Error = "Azure Search service not available",
                        DocumentId = null
                    };
                }

                if (analysis == null)
                {
                    return new MiCasaPhotoUploadResult
                    {
                        Success = false,
                        Error = "Analysis result cannot be null",
                        DocumentId = null
                    };
                }

                var documentId = Guid.NewGuid().ToString();
                _logger.LogInformation("📷 Uploading photo analysis to index: DocumentId={DocumentId}, TwinId={TwinId}, Seccion={Seccion}",
                    documentId, twinId, nombreSeccion);

                // Create search client
                var searchClient = new Azure.Search.Documents.SearchClient(
                    new Uri(_searchEndpoint!),
                    IndexName,
                    new AzureKeyCredential(_searchApiKey!));

                // Generate embeddings from AnalisisDetallado if available
                float[]? embeddings = null;
                if (!string.IsNullOrEmpty(analysis.AnalisisDetallado) && _embeddingClient != null)
                {
                    try
                    {
                        embeddings = await GenerateEmbeddingsAsync(analysis.AnalisisDetallado);
                        if (embeddings != null)
                        {
                            _logger.LogInformation("✅ Generated {Dimensions} dimensional vector embeddings for photo analysis {DocumentId}",
                                embeddings.Length, documentId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Failed to generate embeddings, continuing without vector search");
                    }
                }

                // Create search document with all analysis data as dynamic object
                dynamic searchDocument = new System.Dynamic.ExpandoObject();
                var docDict = (IDictionary<string, object>)searchDocument;

                docDict["id"] = documentId;
                docDict["twinId"] = twinId;
                docDict["tipoSeccion"] = tipoSeccion;
                docDict["nombreSeccion"] = nombreSeccion; 
                docDict["descripcionGenerica"] = analysis.DescripcionGenerica ?? string.Empty;
                docDict["analisisDetallado"] = analysis.AnalisisDetallado ?? string.Empty;
                docDict["analisisDetalladoVector"] = embeddings ?? Array.Empty<float>();
                docDict["calidadGeneral"] = analysis.CalidadGeneral?.Evaluacion ?? string.Empty;
                docDict["estadoGeneral"] = analysis.CalidadGeneral?.Estado ?? string.Empty;
                docDict["tipoPiso"] = analysis.AnalisisPisos?.Tipo ?? string.Empty;
                docDict["calidadPiso"] = analysis.AnalisisPisos?.Calidad ?? string.Empty;
                docDict["cortinas"] = analysis.ElementosDecorativosAcabados?.Cortinas ?? string.Empty;
                docDict["muebles"] = analysis.ElementosDecorativosAcabados?.Muebles ?? string.Empty;
                docDict["iluminacion"] = analysis.ElementosDecorativosAcabados?.Iluminacion ?? string.Empty;
                docDict["limpieza"] = analysis.CondicionesGenerales?.Limpieza ?? string.Empty;
                docDict["habitabilidad"] = analysis.Funcionalidad?.Habitabilidad ?? string.Empty;
                docDict["dimensionesAncho"] = analysis.Dimensiones.Ancho;
                docDict["dimensionesLargo"] = analysis.Dimensiones.Largo;
                docDict["dimensionesAlto"] = analysis.Dimensiones.Alto;
                docDict["dimensionesDiametro"] = analysis.Dimensiones.Diametro;
                docDict["piso"] = analysis.Piso;
                // Add HTML Full Description field
                docDict["htmlFullDescription"] = analysis.HTMLFullDescription ?? string.Empty;

                // Add propiedad ID field
                if (!string.IsNullOrEmpty(propiedadId))
                {
                    docDict["propiedadId"] = propiedadId;
                }

                // Add casa ID field
                if (!string.IsNullOrEmpty(casaId))
                {
                    docDict["casaId"] = casaId;
                }

                // Add file information fields
                if (!string.IsNullOrEmpty(fileName))
                {
                    docDict["fileName"] = fileName;
                }

                if (!string.IsNullOrEmpty(containerName))
                {
                    docDict["containerName"] = containerName;
                }

                if (!string.IsNullOrEmpty(filePath))
                {
                    docDict["filePath"] = filePath;
                }

                if (fileSize.HasValue && fileSize.Value > 0)
                {
                    docDict["fileSize"] = fileSize.Value;
                }

                if (fileUploadedAt.HasValue)
                {
                    docDict["fileUploadedAt"] = fileUploadedAt.Value;
                }

                // Add piso field
                if (!string.IsNullOrEmpty(piso))
                {
                    docDict["piso"] = piso;
                }

                // Add dimensiones fields
                if (dimensionesAncho.HasValue && dimensionesAncho.Value > 0)
                {
                    docDict["dimensionesAncho"] = dimensionesAncho.Value;
                }

                if (dimensionesLargo.HasValue && dimensionesLargo.Value > 0)
                {
                    docDict["dimensionesLargo"] = dimensionesLargo.Value;
                }

                if (dimensionesAlto.HasValue && dimensionesAlto.Value > 0)
                {
                    docDict["dimensionesAlto"] = dimensionesAlto.Value;
                }

                if (dimensionesDiametro.HasValue && dimensionesDiametro.Value > 0)
                {
                    docDict["dimensionesDiametro"] = dimensionesDiametro.Value;
                }

                docDict["analyzedAt"] = DateTimeOffset.UtcNow;

                // Upload document to search index
                var uploadResult = await searchClient.MergeOrUploadDocumentsAsync(new[] { searchDocument });

                var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();

                if (!errors.Any())
                {
                    _logger.LogInformation("✅ Photo analysis uploaded successfully: DocumentId={DocumentId}, TwinId={TwinId}, Seccion={Seccion}",
                        documentId, twinId, nombreSeccion);

                    return new MiCasaPhotoUploadResult
                    {
                        Success = true,
                        Message = $"Photo analysis for '{nombreSeccion}' uploaded successfully to index",
                        IndexName = IndexName,
                        DocumentId = documentId,
                        HasVectorEmbeddings = embeddings != null,
                        VectorDimensions = embeddings?.Length ?? 0
                    };
                }
                else
                {
                    var error = errors.First();
                    _logger.LogError("❌ Error uploading photo analysis {DocumentId}: {Error}",
                        documentId, error.ErrorMessage);

                    return new MiCasaPhotoUploadResult
                    {
                        Success = false,
                        Error = $"Error uploading photo analysis: {error.ErrorMessage}",
                        DocumentId = documentId
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error uploading photo analysis to index");
                return new MiCasaPhotoUploadResult
                {
                    Success = false,
                    Error = $"Error uploading photo analysis: {ex.Message}",
                    DocumentId = null
                };
            }
        }

        /// <summary>
        /// Genera embeddings vectoriales de texto usando Azure OpenAI
        /// </summary>
        private async Task<float[]?> GenerateEmbeddingsAsync(string text)
        {
            try
            {
                if (_embeddingClient == null)
                {
                    _logger.LogWarning("⚠️ Embedding client not available");
                    return null;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("⚠️ Text content is empty");
                    return null;
                }

                // Truncate text if too long (Azure OpenAI has token limits)
                if (text.Length > 8000)
                {
                    text = text.Substring(0, 8000);
                    _logger.LogInformation("📏 Text truncated to 8000 characters for embedding generation");
                }

                var embeddingOptions = new OpenAI.Embeddings.EmbeddingGenerationOptions
                {
                    Dimensions = EmbeddingDimensions
                };

                var embedding = await _embeddingClient.GenerateEmbeddingAsync(text, embeddingOptions);
                var embeddings = embedding.Value.ToFloats().ToArray();

                _logger.LogInformation("✅ Generated embedding vector with {Dimensions} dimensions", embeddings.Length);
                return embeddings;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to generate embeddings");
                return null;
            }
        }

        /// <summary>
        /// Retrieves all photo analysis documents for a specific casa (house) and twin
        /// </summary>
        /// <param name="casaId">ID of the casa (house) to search for</param>
        /// <param name="twinId">ID of the Twin owner to filter by</param>
        /// <returns>Result containing all documents found for the casa and twin</returns>
        public async Task<MiCasaPhotosByHouseResult> GetPhotosByCasaIdAsync(string casaId, string twinId)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new MiCasaPhotosByHouseResult
                    {
                        Success = false,
                        Error = "Azure Search service not available",
                        Documents = new List<MiCasaPhotoDocument>()
                    };
                }

                if (string.IsNullOrWhiteSpace(casaId))
                {
                    return new MiCasaPhotosByHouseResult
                    {
                        Success = false,
                        Error = "casaId cannot be null or empty",
                        Documents = new List<MiCasaPhotoDocument>()
                    };
                }

                if (string.IsNullOrWhiteSpace(twinId))
                {
                    return new MiCasaPhotosByHouseResult
                    {
                        Success = false,
                        Error = "twinId cannot be null or empty",
                        Documents = new List<MiCasaPhotoDocument>()
                    };
                }

                _logger.LogInformation("🔍 Retrieving all photo analysis documents for casaId: {CasaId}, twinId: {TwinId}", casaId, twinId);

                // Create search client
                var searchClient = new Azure.Search.Documents.SearchClient(
                    new Uri(_searchEndpoint!),
                    IndexName,
                    new AzureKeyCredential(_searchApiKey!));

                // Build filter expression for both casaId and twinId
                var filterExpression = $"casaId eq '{casaId}' and twinId eq '{twinId}'";

                // Execute search with filter
                var searchOptions = new SearchOptions
                {
                    Filter = filterExpression,
                    Size = 1000, // Get up to 1000 documents per request
                    IncludeTotalCount = true
                };

                var searchResults = await searchClient.SearchAsync<MiCasaPhotoDocument>(
                    "*", // Search all documents with the filter
                    searchOptions);

                var documents = new List<MiCasaPhotoDocument>();
                var totalCount = searchResults.Value.TotalCount ?? 0;

                // Collect all results
                await foreach (var result in searchResults.Value.GetResultsAsync())
                {
                    documents.Add(result.Document);
                }

                // Generate SAS URLs for all documents
                if (documents.Count > 0)
                {
                    try
                    {
                        // Create DataLakeClient factory
                        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                        var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                        var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                        // Generate SAS URL for each document
                        foreach (var doc in documents)
                        {
                            // Initialize the DesignedImagesSAS list
                            doc.DesignedImagesSAS = new List<string>();
                            // Initialize the Fotos list
                            doc.Fotos = new List<BlobFileInfo>();

                            if (!string.IsNullOrEmpty(doc.FilePath))
                            {
                                // Get all files in the directory
                                _logger.LogInformation("📂 Listing all files in directory: {DirectoryPath}", doc.FilePath);
                                var filesInDirectory = await dataLakeClient.ListFilesInDirectoryAsync(doc.FilePath);

                                _logger.LogInformation("✅ Found {Count} total files in directory", filesInDirectory.Count);

                                // Filter for image files only (common image extensions)
                                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif" };
                                var imageFiles = filesInDirectory.Where(f => 
                                {
                                    var extension = Path.GetExtension(f.Name).ToLowerInvariant();
                                    return imageExtensions.Contains(extension);
                                }).ToList();

                                _logger.LogInformation("🖼️ Found {Count} image files in directory", imageFiles.Count);

                                // Separate original image from designed images
                                foreach (var imageFile in imageFiles)
                                {
                                    // Extract just the filename from the full path
                                    var imageFileName = Path.GetFileName(imageFile.Name);
                                    
                                    // Generate SAS URL for this image
                                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync(imageFile.Name, TimeSpan.FromHours(24));
                                    
                                    // Create BlobFileInfo and add to Fotos list
                                    var blobInfo = new BlobFileInfo
                                    {
                                        Name = imageFile.Name,
                                        Size = imageFile.Size,
                                        ContentType = imageFile.ContentType,
                                        LastModified = imageFile.LastModified,
                                        CreatedOn = imageFile.CreatedOn,
                                        ETag = imageFile.ETag,
                                        Metadata = imageFile.Metadata,
                                        Url = sasUrl ?? string.Empty
                                    };
                                    doc.Fotos.Add(blobInfo);
                                    
                                    // Check if this is the original image
                                    if (!string.IsNullOrEmpty(doc.FileName) && 
                                        imageFileName.Equals(doc.FileName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        // This is the original image - generate SAS URL for doc.URL
                                        doc.URL = sasUrl;
                                        
                                        if (string.IsNullOrEmpty(doc.URL))
                                        {
                                            _logger.LogWarning("⚠️ Failed to generate SAS URL for original image: {DocumentId}, File: {FileName}", 
                                                doc.Id, imageFileName);
                                        }
                                        else
                                        {
                                            _logger.LogInformation("✅ Generated SAS URL for original image: {FileName}", imageFileName);
                                        }
                                    }
                                    else
                                    {
                                        // This is a designed/redesigned image - add to DesignedImagesSAS list
                                        if (!string.IsNullOrEmpty(sasUrl))
                                        {
                                            doc.DesignedImagesSAS.Add(sasUrl);
                                            _logger.LogInformation("✅ Added designed image SAS URL: {FileName}", imageFileName);
                                        }
                                        else
                                        {
                                            _logger.LogWarning("⚠️ Failed to generate SAS URL for designed image: {FileName}", imageFileName);
                                        }
                                    }
                                }

                                _logger.LogInformation("📊 Document {DocumentId}: Original URL set: {HasOriginal}, Designed images: {DesignedCount}, Total Fotos: {FotosCount}", 
                                    doc.Id, !string.IsNullOrEmpty(doc.URL), doc.DesignedImagesSAS.Count, doc.Fotos.Count);
                            }
                            else if (!string.IsNullOrEmpty(doc.FileName))
                            {
                                // Fallback: If no FilePath but has FileName, try to generate SAS URL directamente
                                doc.URL = await dataLakeClient.GenerateSasUrlAsync(doc.FileName, TimeSpan.FromHours(24));
                                
                                if (string.IsNullOrEmpty(doc.URL))
                                {
                                    _logger.LogWarning("⚠️ Failed to generate SAS URL for document (no FilePath): {DocumentId}", doc.Id);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error generating SAS URLs for documents");
                        // Continue without SAS URLs - don't fail the entire operation
                    }
                }

                if (documents.Count > 0)
                {
                    _logger.LogInformation("✅ Found {DocumentCount} photo analysis documents for casaId: {CasaId}, twinId: {TwinId}",
                        documents.Count, casaId, twinId);

                    return new MiCasaPhotosByHouseResult
                    {
                        Success = true,
                        Message = $"Successfully retrieved {documents.Count} documents for casa '{casaId}' and twin '{twinId}'",
                        IndexName = IndexName,
                        CasaId = casaId,
                        TwinId = twinId,
                        TotalCount = totalCount,
                        Documents = documents
                    };
                }
                else
                {
                    _logger.LogWarning("⚠️ No documents found for casaId: {CasaId}, twinId: {TwinId}", casaId, twinId);

                    return new MiCasaPhotosByHouseResult
                    {
                        Success = true,
                        Message = $"No documents found for casa '{casaId}' and twin '{twinId}'",
                        IndexName = IndexName,
                        CasaId = casaId,
                        TwinId = twinId,
                        TotalCount = 0,
                        Documents = new List<MiCasaPhotoDocument>()
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving photo analysis documents for casaId: {CasaId}, twinId: {TwinId}", casaId, twinId);
                return new MiCasaPhotosByHouseResult
                {
                    Success = false,
                    Error = $"Error retrieving documents: {ex.Message}",
                    Documents = new List<MiCasaPhotoDocument>()
                };
            }
        }
     
     /// </summary>
        /// <param name="casaId">ID of the casa (house) to search for</param>
        /// <param name="twinId">ID of the Twin owner to filter by</param>
        /// <returns>Result containing all documents found for the casa and twin</returns>
        public async Task<MiCasaPhotosByHouseResult> GetPhotosByCasaIdPropiedadIdAsync(string casaId,
            string twinId, string PropiedadID)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new MiCasaPhotosByHouseResult
                    {
                        Success = false,
                        Error = "Azure Search service not available",
                        Documents = new List<MiCasaPhotoDocument>()
                    };
                }

                if (string.IsNullOrWhiteSpace(casaId))
                {
                    return new MiCasaPhotosByHouseResult
                    {
                        Success = false,
                        Error = "casaId cannot be null or empty",
                        Documents = new List<MiCasaPhotoDocument>()
                    };
                }

                if (string.IsNullOrWhiteSpace(twinId))
                {
                    return new MiCasaPhotosByHouseResult
                    {
                        Success = false,
                        Error = "twinId cannot be null or empty",
                        Documents = new List<MiCasaPhotoDocument>()
                    };
                }

                _logger.LogInformation("🔍 Retrieving all photo analysis documents for casaId: {CasaId}, twinId: {TwinId}", casaId, twinId);

                // Create search client
                var searchClient = new Azure.Search.Documents.SearchClient(
                    new Uri(_searchEndpoint!),
                    IndexName,
                    new AzureKeyCredential(_searchApiKey!));

                // Build filter expression for both casaId and twinId
                var filterExpression = $"casaId eq '{casaId}' " +
                    $" and propiedadId eq '{PropiedadID}' " +
                    $" and twinId eq '{twinId}'";

                // Execute search with filter
                var searchOptions = new SearchOptions
                {
                    Filter = filterExpression,
                    Size = 1000, // Get up to 1000 documents per request
                    IncludeTotalCount = true
                };

                var searchResults = await searchClient.SearchAsync<MiCasaPhotoDocument>(
                    "*", // Search all documents with the filter
                    searchOptions);

                var documents = new List<MiCasaPhotoDocument>();
                var totalCount = searchResults.Value.TotalCount ?? 0;

                // Collect all results
                await foreach (var result in searchResults.Value.GetResultsAsync())
                {
                    documents.Add(result.Document);
                }

                // Generate SAS URLs for all documents
                if (documents.Count > 0)
                {
                    try
                    {
                        // Create DataLakeClient factory
                        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                        var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                        var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                        // Generate SAS URL for each document
                        foreach (var doc in documents)
                        {
                            // Initialize the DesignedImagesSAS list
                            doc.DesignedImagesSAS = new List<string>();
                            // Initialize the Fotos list
                            doc.Fotos = new List<BlobFileInfo>();

                            if (!string.IsNullOrEmpty(doc.FilePath))
                            {
                                // Get all files in the directory
                                _logger.LogInformation("📂 Listing all files in directory: {DirectoryPath}", doc.FilePath);
                                var filesInDirectory = await dataLakeClient.ListFilesInDirectoryAsync(doc.FilePath);

                                _logger.LogInformation("✅ Found {Count} total files in directory", filesInDirectory.Count);

                                // Filter for image files only (common image extensions)
                                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif" };
                                var imageFiles = filesInDirectory.Where(f => 
                                {
                                    var extension = Path.GetExtension(f.Name).ToLowerInvariant();
                                    return imageExtensions.Contains(extension);
                                }).ToList();

                                _logger.LogInformation("🖼️ Found {Count} image files in directory", imageFiles.Count);

                                // Separate original image from designed images
                                foreach (var imageFile in imageFiles)
                                {
                                    // Extract just the filename from the full path
                                    var imageFileName = Path.GetFileName(imageFile.Name);
                                    
                                    // Generate SAS URL for this image
                                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync(imageFile.Name, TimeSpan.FromHours(24));
                                    
                                    // Create BlobFileInfo and add to Fotos list
                                    var blobInfo = new BlobFileInfo
                                    {
                                        Name = imageFile.Name,
                                        Size = imageFile.Size,
                                        ContentType = imageFile.ContentType,
                                        LastModified = imageFile.LastModified,
                                        CreatedOn = imageFile.CreatedOn,
                                        ETag = imageFile.ETag,
                                        Metadata = imageFile.Metadata,
                                        Url = sasUrl ?? string.Empty
                                    };
                                    doc.Fotos.Add(blobInfo);
                                    
                                    // Check if this is the original image
                                    if (!string.IsNullOrEmpty(doc.FileName) && 
                                        imageFileName.Equals(doc.FileName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        // This is the original image - generate SAS URL for doc.URL
                                        doc.URL = sasUrl;
                                        
                                        if (string.IsNullOrEmpty(doc.URL))
                                        {
                                            _logger.LogWarning("⚠️ Failed to generate SAS URL for original image: {DocumentId}, File: {FileName}", 
                                                doc.Id, imageFileName);
                                        }
                                        else
                                        {
                                            _logger.LogInformation("✅ Generated SAS URL for original image: {FileName}", imageFileName);
                                        }
                                    }
                                    else
                                    {
                                        // This is a designed/redesigned image - add to DesignedImagesSAS list
                                        if (!string.IsNullOrEmpty(sasUrl))
                                        {
                                            doc.DesignedImagesSAS.Add(sasUrl);
                                            _logger.LogInformation("✅ Added designed image SAS URL: {FileName}", imageFileName);
                                        }
                                        else
                                        {
                                            _logger.LogWarning("⚠️ Failed to generate SAS URL for designed image: {FileName}", imageFileName);
                                        }
                                    }
                                }

                                _logger.LogInformation("📊 Document {DocumentId}: Original URL set: {HasOriginal}, Designed images: {DesignedCount}, Total Fotos: {FotosCount}", 
                                    doc.Id, !string.IsNullOrEmpty(doc.URL), doc.DesignedImagesSAS.Count, doc.Fotos.Count);
                            }
                            else if (!string.IsNullOrEmpty(doc.FileName))
                            {
                                // Fallback: If no FilePath but has FileName, try to generate SAS URL directamente
                                doc.URL = await dataLakeClient.GenerateSasUrlAsync(doc.FileName, TimeSpan.FromHours(24));
                                
                                if (string.IsNullOrEmpty(doc.URL))
                                {
                                    _logger.LogWarning("⚠️ Failed to generate SAS URL for document (no FilePath): {DocumentId}", doc.Id);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error generating SAS URLs for documents");
                        // Continue without SAS URLs - don't fail the entire operation
                    }
                }

                if (documents.Count > 0)
                {
                    _logger.LogInformation("✅ Found {DocumentCount} photo analysis documents for casaId: {CasaId}, twinId: {TwinId}",
                        documents.Count, casaId, twinId);

                    return new MiCasaPhotosByHouseResult
                    {
                        Success = true,
                        Message = $"Successfully retrieved {documents.Count} documents for casa '{casaId}' and twin '{twinId}'",
                        IndexName = IndexName,
                        CasaId = casaId,
                        TwinId = twinId,
                        TotalCount = totalCount,
                        Documents = documents
                    };
                }
                else
                {
                    _logger.LogWarning("⚠️ No documents found for casaId: {CasaId}, twinId: {TwinId}", casaId, twinId);

                    return new MiCasaPhotosByHouseResult
                    {
                        Success = true,
                        Message = $"No documents found for casa '{casaId}' and twin '{twinId}'",
                        IndexName = IndexName,
                        CasaId = casaId,
                        TwinId = twinId,
                        TotalCount = 0,
                        Documents = new List<MiCasaPhotoDocument>()
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving photo analysis documents for casaId: {CasaId}, twinId: {TwinId}", casaId, twinId);
                return new MiCasaPhotosByHouseResult
                {
                    Success = false,
                    Error = $"Error retrieving documents: {ex.Message}",
                    Documents = new List<MiCasaPhotoDocument>()
                };
            }
        }

        /// <summary>
        /// Updates an existing photo analysis document in the Azure Search index
        /// </summary>
        /// <param name="documentId">ID of the document to update</param>
        /// <param name="photoDocument">Updated MiCasaPhotoDocument with new values</param>
        /// <returns>Result of the update operation</returns>
        public async Task<MiCasaPhotoUploadResult> UpdatePhotoAnalysisInIndexAsync(
            string documentId,
            MiCasaPhotoDocument photoDocument)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new MiCasaPhotoUploadResult
                    {
                        Success = false,
                        Error = "Azure Search service not available",
                        DocumentId = null
                    };
                }

                if (string.IsNullOrEmpty(documentId))
                {
                    return new MiCasaPhotoUploadResult
                    {
                        Success = false,
                        Error = "Document ID cannot be null or empty",
                        DocumentId = null
                    };
                }

                if (photoDocument == null)
                {
                    return new MiCasaPhotoUploadResult
                    {
                        Success = false,
                        Error = "Photo document cannot be null",
                        DocumentId = null
                    };
                }

                _logger.LogInformation("📷 Updating photo analysis document in index: DocumentId={DocumentId}, Seccion={Seccion}",
                    documentId, photoDocument.NombreSeccion);

                // Create search client
                var searchClient = new Azure.Search.Documents.SearchClient(
                    new Uri(_searchEndpoint!),
                    IndexName,
                    new AzureKeyCredential(_searchApiKey!));

                // Ensure document has the correct ID
                photoDocument.Id = documentId;

                // Create dynamic object from MiCasaPhotoDocument for update
                dynamic searchDocument = new System.Dynamic.ExpandoObject();
                var docDict = (IDictionary<string, object>)searchDocument;

                // Map all properties from MiCasaPhotoDocument to dynamic object
                docDict["id"] = photoDocument.Id;
                docDict["twinId"] = photoDocument.TwinId ?? string.Empty;
                docDict["tipoSeccion"] = photoDocument.TipoSeccion ?? string.Empty;
                docDict["nombreSeccion"] = photoDocument.NombreSeccion ?? string.Empty;
                docDict["piso"] = photoDocument.Piso ?? string.Empty;
                docDict["descripcionGenerica"] = photoDocument.DescripcionGenerica ?? string.Empty;
                docDict["analisisDetallado"] = photoDocument.AnalisisDetallado ?? string.Empty;
                docDict["analisisDetalladoVector"] = photoDocument.AnalisisDetalladoVector ?? Array.Empty<float>();
                docDict["calidadGeneral"] = photoDocument.CalidadGeneral ?? string.Empty;
                docDict["estadoGeneral"] = photoDocument.EstadoGeneral ?? string.Empty;
                docDict["tipoPiso"] = photoDocument.TipoPiso ?? string.Empty;
                docDict["calidadPiso"] = photoDocument.CalidadPiso ?? string.Empty;
                docDict["cortinas"] = photoDocument.Cortinas ?? string.Empty;
                docDict["muebles"] = photoDocument.Muebles ?? string.Empty;
                docDict["iluminacion"] = photoDocument.Iluminacion ?? string.Empty;
                docDict["limpieza"] = photoDocument.Limpieza ?? string.Empty;
                docDict["habitabilidad"] = photoDocument.Habitabilidad ?? string.Empty;
                docDict["htmlFullDescription"] = photoDocument.HtmlFullDescription ?? string.Empty;
                docDict["propiedadId"] = photoDocument.PropiedadId ?? string.Empty;
                docDict["casaId"] = photoDocument.CasaId ?? string.Empty;
                docDict["fileName"] = photoDocument.FileName ?? string.Empty;
                docDict["containerName"] = photoDocument.ContainerName ?? string.Empty;
                docDict["filePath"] = photoDocument.FilePath ?? string.Empty;
                docDict["fileSize"] = photoDocument.FileSize ?? 0;
                docDict["fileUploadedAt"] = photoDocument.FileUploadedAt ?? DateTimeOffset.UtcNow;
                docDict["analyzedAt"] = photoDocument.AnalyzedAt ?? DateTimeOffset.UtcNow;
                docDict["dimensionesAncho"] = photoDocument.DimensionesAncho ?? 0.0;
                docDict["dimensionesLargo"] = photoDocument.DimensionesLargo ?? 0.0;
                docDict["dimensionesAlto"] = photoDocument.DimensionesAlto ?? 0.0;
                docDict["dimensionesDiametro"] = photoDocument.DimensionesDiametro ?? 0.0;

                // Update document in search index using MergeOrUpload
                var uploadResult = await searchClient.MergeOrUploadDocumentsAsync(new[] { searchDocument });

                var errors = uploadResult.Value.Results.Where(r => !r.Succeeded).ToList();

                if (!errors.Any())
                {
                    _logger.LogInformation("✅ Photo analysis document updated successfully: DocumentId={DocumentId}, Seccion={Seccion}",
                        documentId, photoDocument.NombreSeccion);

                    return new MiCasaPhotoUploadResult
                    {
                        Success = true,
                        Message = $"Photo analysis document '{photoDocument.NombreSeccion}' updated successfully in index",
                        IndexName = IndexName,
                        DocumentId = documentId,
                        HasVectorEmbeddings = photoDocument.AnalisisDetalladoVector != null && photoDocument.AnalisisDetalladoVector.Count > 0,
                        VectorDimensions = photoDocument.AnalisisDetalladoVector?.Count ?? 0
                    };
                }
                else
                {
                    var error = errors.First();
                    _logger.LogError("❌ Error updating photo analysis document {DocumentId}: {Error}",
                        documentId, error.ErrorMessage);

                    return new MiCasaPhotoUploadResult
                    {
                        Success = false,
                        Error = $"Error updating photo analysis document: {error.ErrorMessage}",
                        DocumentId = documentId
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating photo analysis document in index");
                return new MiCasaPhotoUploadResult
                {
                    Success = false,
                    Error = $"Error updating photo analysis document: {ex.Message}",
                    DocumentId = documentId
                };
            }
        }

        /// <summary>
        /// Deletes a photo analysis document from the Azure Search index by document ID and twin ID
        /// </summary>
        /// <param name="documentId">ID of the document to delete</param>
        /// <param name="twinId">Twin ID for validation (document must belong to this twin)</param>
        /// <returns>Result of the delete operation</returns>
        public async Task<MiCasaPhotoUploadResult> DeletePhotoAnalysisFromIndexAsync(
            string documentId,
            string twinId)
        {
            try
            {
                if (!IsAvailable)
                {
                    return new MiCasaPhotoUploadResult
                    {
                        Success = false,
                        Error = "Azure Search service not available",
                        DocumentId = documentId
                    };
                }

                if (string.IsNullOrEmpty(documentId))
                {
                    return new MiCasaPhotoUploadResult
                    {
                        Success = false,
                        Error = "Document ID cannot be null or empty",
                        DocumentId = null
                    };
                }

                if (string.IsNullOrEmpty(twinId))
                {
                    return new MiCasaPhotoUploadResult
                    {
                        Success = false,
                        Error = "Twin ID cannot be null or empty",
                        DocumentId = documentId
                    };
                }

                _logger.LogInformation("🗑️ Deleting photo analysis document from index: DocumentId={DocumentId}, TwinId={TwinId}",
                    documentId, twinId);

                // Create search client
                var searchClient = new Azure.Search.Documents.SearchClient(
                    new Uri(_searchEndpoint!),
                    IndexName,
                    new AzureKeyCredential(_searchApiKey!));

                // First, retrieve the document to verify it belongs to the specified twin
                var searchOptions = new SearchOptions
                {
                    Filter = $"id eq '{documentId}' and twinId eq '{twinId}'"
                };

                var searchResults = await searchClient.SearchAsync<MiCasaPhotoDocument>(
                    "*",
                    searchOptions);

                var documents = new List<MiCasaPhotoDocument>();
                await foreach (var result in searchResults.Value.GetResultsAsync())
                {
                    documents.Add(result.Document);
                }

                if (documents.Count == 0)
                {
                    _logger.LogWarning("⚠️ Document not found or does not belong to the specified twin: DocumentId={DocumentId}, TwinId={TwinId}",
                        documentId, twinId);

                    return new MiCasaPhotoUploadResult
                    {
                        Success = false,
                        Error = $"Document with ID '{documentId}' not found for Twin '{twinId}'",
                        DocumentId = documentId
                    };
                }

                // Document exists and belongs to the twin, proceed with deletion
                // Create document object with ID for deletion
                dynamic deleteDocument = new System.Dynamic.ExpandoObject();
                var deleteDocDict = (IDictionary<string, object>)deleteDocument;
                deleteDocDict["id"] = documentId;

                var deleteResult = await searchClient.DeleteDocumentsAsync(
                    new List<dynamic> { deleteDocument });

                var errors = deleteResult.Value.Results.Where(r => !r.Succeeded).ToList();

                if (!errors.Any())
                {
                    _logger.LogInformation("✅ Photo analysis document deleted successfully: DocumentId={DocumentId}, TwinId={TwinId}",
                        documentId, twinId);

                    return new MiCasaPhotoUploadResult
                    {
                        Success = true,
                        Message = $"Photo analysis document '{documentId}' deleted successfully from index",
                        IndexName = IndexName,
                        DocumentId = documentId,
                        HasVectorEmbeddings = false,
                        VectorDimensions = 0
                    };
                }
                else
                {
                    var error = errors.First();
                    _logger.LogError("❌ Error deleting photo analysis document {DocumentId}: {Error}",
                        documentId, error.ErrorMessage);

                    return new MiCasaPhotoUploadResult
                    {
                        Success = false,
                        Error = $"Error deleting photo analysis document: {error.ErrorMessage}",
                        DocumentId = documentId
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting photo analysis document from index: DocumentId={DocumentId}, TwinId={TwinId}",
                    documentId, twinId);

                return new MiCasaPhotoUploadResult
                {
                    Success = false,
                    Error = $"Error deleting photo analysis document: {ex.Message}",
                    DocumentId = documentId
                };
            }
        }
    }
 

    /// <summary>
    /// Result class for MiCasa Fotos index operations
    /// </summary>
    public class MiCasaIndexResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public string? IndexName { get; set; }
        public int FieldsCount { get; set; }
        public bool HasVectorSearch { get; set; }
        public bool HasSemanticSearch { get; set; }
    }

    /// <summary>
    /// Result class for photo analysis upload operations
    /// </summary>
    public class MiCasaPhotoUploadResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public string? IndexName { get; set; }
        public string? DocumentId { get; set; }
        public bool HasVectorEmbeddings { get; set; }
        public int VectorDimensions { get; set; }
    }

    /// <summary>
    /// Represents a photo analysis document retrieved from the MiCasa Fotos Index
    /// </summary>
    public class MiCasaPhotoDocument
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string? Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("twinId")]
        public string? TwinId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tipoSeccion")]
        public string? TipoSeccion { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("nombreSeccion")]
        public string? NombreSeccion { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("piso")]
        public string? Piso { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("descripcionGenerica")]
        public string? DescripcionGenerica { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("analisisDetallado")]
        public string? AnalisisDetallado { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("analisisDetalladoVector")]
        public IList<float>? AnalisisDetalladoVector { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("calidadGeneral")]
        public string? CalidadGeneral { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("estadoGeneral")]
        public string? EstadoGeneral { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tipoPiso")]
        public string? TipoPiso { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("calidadPiso")]
        public string? CalidadPiso { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("cortinas")]
        public string? Cortinas { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("muebles")]
        public string? Muebles { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("iluminacion")]
        public string? Iluminacion { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("limpieza")]
        public string? Limpieza { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("habitabilidad")]
        public string? Habitabilidad { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("htmlFullDescription")]
        public string? HtmlFullDescription { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("propiedadId")]
        public string? PropiedadId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("casaId")]
        public string? CasaId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("fileName")]
        public string? FileName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("containerName")]
        public string? ContainerName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("filePath")]
        public string? FilePath { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("url")]
        public string? URL { get; set; }


        [System.Text.Json.Serialization.JsonPropertyName("designedImagesSAS")]
        public List<string> DesignedImagesSAS { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("fotos")]
        public List<BlobFileInfo> Fotos { get; set; } = new List<BlobFileInfo>();

        [System.Text.Json.Serialization.JsonPropertyName("fileSize")]
        public long? FileSize { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("fileUploadedAt")]
        public DateTimeOffset? FileUploadedAt { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("analyzedAt")]
        public DateTimeOffset? AnalyzedAt { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dimensionesAncho")]
        public double? DimensionesAncho { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dimensionesLargo")]
        public double? DimensionesLargo { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dimensionesAlto")]
        public double? DimensionesAlto { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dimensionesDiametro")]
        public double? DimensionesDiametro { get; set; }
    }

    /// <summary>
    /// Información de archivo blob almacenado en Azure Storage
    /// </summary>
    public class BlobFileInfo
    {
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ETag { get; set; } = string.Empty;
        public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
        public string Url { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result class for retrieving photo analysis documents by casa (house) ID
    /// </summary>
    public class MiCasaPhotosByHouseResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public string? IndexName { get; set; }
        public string? CasaId { get; set; }
        public string? TwinId { get; set; }
        public long TotalCount { get; set; }
        public List<MiCasaPhotoDocument> Documents { get; set; } = new List<MiCasaPhotoDocument>();
    }

    /// <summary>
    /// Extensión estática para crear DataLakeClientFactory desde IConfiguration
    /// </summary>
    public static class DataLakeConfigurationExtensions
    {
        public static DataLakeClientFactory CreateDataLakeFactory(this IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            var storageSettings = new AzureStorageSettings
            {
                AccountName = configuration["AZURE_STORAGE_ACCOUNT_NAME"] ?? configuration["Values:AZURE_STORAGE_ACCOUNT_NAME"] ?? "",
                AccountKey = configuration["AZURE_STORAGE_ACCOUNT_KEY"] ?? configuration["Values:AZURE_STORAGE_ACCOUNT_KEY"] ?? ""
            };

            var options = Microsoft.Extensions.Options.Options.Create(storageSettings);
            return new DataLakeClientFactory(loggerFactory, options);
        }
    }
}

