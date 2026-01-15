using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace TwinAgentsNetwork.Models
{
    /// <summary>
    /// Resultado completo del análisis de foto MiCasa generado por IA
    /// Mapea el JSON completo producido por Azure OpenAI Vision
    /// </summary>
    public class MiCasaPhotoAnalysisResult
    {
        /// <summary>
        /// Descripción breve genérica del espacio (máx 100 caracteres)
        /// </summary>
        [JsonProperty("descripcionGenerica")]
        public string DescripcionGenerica { get; set; } = string.Empty;

        /// <summary>
        /// Elementos visuales detectados en la imagen
        /// </summary>
        [JsonProperty("elementosVisuales")]
        public ElementosVisuales ElementosVisuales { get; set; } = new();

        /// <summary>
        /// Análisis detallado del piso y revestimientos
        /// </summary>
        [JsonProperty("analisisPisos")]
        public AnalisisPisos AnalisisPisos { get; set; } = new();

        /// <summary>
        /// Análisis de elementos decorativos y acabados
        /// </summary>
        [JsonProperty("elementosDecorativosAcabados")]
        public ElementosDecorativosAcabados ElementosDecorativosAcabados { get; set; } = new();

        /// <summary>
        /// Evaluación de condiciones generales del espacio
        /// </summary>
        [JsonProperty("condicionesGenerales")]
        public CondicionesGenerales CondicionesGenerales { get; set; } = new();

        /// <summary>
        /// Evaluación de funcionalidad del espacio
        /// </summary>
        [JsonProperty("funcionalidad")]
        public Funcionalidad Funcionalidad { get; set; } = new();

        /// <summary>
        /// Evaluación general de calidad
        /// </summary>
        [JsonProperty("calidadGeneral")]
        public CalidadGeneral CalidadGeneral { get; set; } = new();

        /// <summary>
        /// Validación de coincidencias y discrepancias con contexto
        /// </summary>
        [JsonProperty("validacionContexto")]
        public ValidacionContexto ValidacionContexto { get; set; } = new();

       
        
        /// <summary>
        /// Datos estructurados extraídos de la imagen
        /// </summary>
        [JsonProperty("datos")]
        public List<DatoExtraido> Datos { get; set; } = new();

        /// <summary>
        /// Análisis técnico detallado del estado y condiciones
        /// </summary>
        [JsonProperty("analisisDetallado")]
        public string AnalisisDetallado { get; set; } = string.Empty;

        /// <summary>
        /// Descripción completa en formato HTML con estilos CSS inline
        /// </summary>
        [JsonProperty("HTMLFullDescription")]
        public string HTMLFullDescription { get; set; } = string.Empty;


        [JsonProperty("piso")]
        public string Piso { get; set; } = string.Empty;

        /// <summary>
        /// Dimensiones del espacio detectadas en la imagen
        /// </summary>
        [JsonProperty("dimensiones")]
        public DimensionesAnalisis Dimensiones { get; set; } = new();

        /// <summary>
        /// Metros cuadrados calculados del espacio
        /// </summary>
        [JsonProperty("metrosCuadrados")]
        public double? MetrosCuadrados { get; set; }
    }

    /// <summary>
    /// Elementos visuales detectados en la fotografía
    /// </summary>
    public class ElementosVisuales
    {
        /// <summary>
        /// Cantidad de personas visible en la imagen
        /// </summary>
        [JsonProperty("cantidadPersonas")]
        public int CantidadPersonas { get; set; }

        /// <summary>
        /// Lista de objetos destacados detectados
        /// </summary>
        [JsonProperty("objetos")]
        public List<string> Objetos { get; set; } = new();

        /// <summary>
        /// Descripción detallada del escenario/entorno
        /// </summary>
        [JsonProperty("escenario")]
        public string Escenario { get; set; } = string.Empty;

        /// <summary>
        /// Características específicas del espacio
        /// </summary>
        [JsonProperty("caracteristicas")]
        public List<string> Caracteristicas { get; set; } = new();
    }

    /// <summary>
    /// Análisis del piso y revestimientos
    /// </summary>
    public class AnalisisPisos
    {
        /// <summary>
        /// Tipo de piso detectado (cerámica, madera, laminado, etc.)
        /// </summary>
        [JsonProperty("tipo")]
        public string Tipo { get; set; } = string.Empty;

        /// <summary>
        /// Calidad del piso: excelente/buena/regular/deficiente
        /// </summary>
        [JsonProperty("calidad")]
        public string Calidad { get; set; } = string.Empty;

        /// <summary>
        /// Estado actual del piso (manchas, grietas, desgaste, etc.)
        /// </summary>
        [JsonProperty("estado")]
        public string Estado { get; set; } = string.Empty;

        /// <summary>
        /// Nivel de conservación del piso
        /// </summary>
        [JsonProperty("conservacion")]
        public string Conservacion { get; set; } = string.Empty;
    }

    /// <summary>
    /// Análisis de elementos decorativos y acabados
    /// </summary>
    public class ElementosDecorativosAcabados
    {
        /// <summary>
        /// Descripción de cortinas/persianas o "No visible"
        /// </summary>
        [JsonProperty("cortinas")]
        public string Cortinas { get; set; } = string.Empty;

        /// <summary>
        /// Descripción de mobiliario visible
        /// </summary>
        [JsonProperty("muebles")]
        public string Muebles { get; set; } = string.Empty;

        /// <summary>
        /// Tipos de iluminación detectados
        /// </summary>
        [JsonProperty("iluminacion")]
        public string Iluminacion { get; set; } = string.Empty;

        /// <summary>
        /// Otros elementos decorativos (cuadros, espejos, adornos)
        /// </summary>
        [JsonProperty("decoracion")]
        public string Decoracion { get; set; } = string.Empty;

        /// <summary>
        /// Presencia de plantas o elementos verdes
        /// </summary>
        [JsonProperty("plantas")]
        public string Plantas { get; set; } = string.Empty;
    }

    /// <summary>
    /// Evaluación de condiciones generales del espacio
    /// </summary>
    public class CondicionesGenerales
    {
        /// <summary>
        /// Evaluación del nivel de limpieza y orden
        /// </summary>
        [JsonProperty("limpieza")]
        public string Limpieza { get; set; } = string.Empty;

        /// <summary>
        /// Indicios de mantenimiento o abandono
        /// </summary>
        [JsonProperty("mantenimiento")]
        public string Mantenimiento { get; set; } = string.Empty;

        /// <summary>
        /// Lista de problemas visibles detectados
        /// </summary>
        [JsonProperty("problemasVisibles")]
        public List<string> ProblemasVisibles { get; set; } = new();

        /// <summary>
        /// Impresión general de amplitud del espacio
        /// </summary>
        [JsonProperty("amplitud")]
        public string Amplitud { get; set; } = string.Empty;

        /// <summary>
        /// Colores predominantes en el espacio
        /// </summary>
        [JsonProperty("coloresPredominantes")]
        public List<string> ColoresPredominantes { get; set; } = new();
    }

    /// <summary>
    /// Evaluación de funcionalidad del espacio
    /// </summary>
    public class Funcionalidad
    {
        /// <summary>
        /// ¿Parece funcional y habitable? (sí/no/parcialmente)
        /// </summary>
        [JsonProperty("habitabilidad")]
        public string Habitabilidad { get; set; } = string.Empty;

        /// <summary>
        /// Evaluación de la organización del espacio
        /// </summary>
        [JsonProperty("organizacion")]
        public string Organizacion { get; set; } = string.Empty;

        /// <summary>
        /// Potencial de mejora y recomendaciones
        /// </summary>
        [JsonProperty("potencialMejora")]
        public string PotencialMejora { get; set; } = string.Empty;
    }

    /// <summary>
    /// Evaluación general de calidad del espacio
    /// </summary>
    public class CalidadGeneral
    {
        /// <summary>
        /// Evaluación de calidad: Premium/Alta/Media/Baja
        /// </summary>
        [JsonProperty("evaluacion")]
        public string Evaluacion { get; set; } = string.Empty;

        /// <summary>
        /// Indicadores de inversión en mantenimiento y calidad
        /// </summary>
        [JsonProperty("indicadoresCalidad")]
        public List<string> IndicadoresCalidad { get; set; } = new();

        /// <summary>
        /// Descripción del estado general
        /// </summary>
        [JsonProperty("estado")]
        public string Estado { get; set; } = string.Empty;
    }

    /// <summary>
    /// Validación de coincidencias y discrepancias con contexto
    /// </summary>
    public class ValidacionContexto
    {
        /// <summary>
        /// Aspectos que coinciden con el contexto proporcionado
        /// </summary>
        [JsonProperty("coincidencias")]
        public List<string> Coincidencias { get; set; } = new();

        /// <summary>
        /// Diferencias o discrepancias entre lo esperado y lo visible
        /// </summary>
        [JsonProperty("discrepancias")]
        public List<string> Discrepancias { get; set; } = new();

        /// <summary>
        /// Observaciones adicionales sobre la validación
        /// </summary>
        [JsonProperty("observacionesAdicionales")]
        public string ObservacionesAdicionales { get; set; } = string.Empty;
    }

    /// <summary>
    /// Dato individual extraído de la imagen
    /// </summary>
    public class DatoExtraido
    {
        /// <summary>
        /// Nombre del dato (ej: "Calidad de Piso")
        /// </summary>
        [JsonProperty("NombreDato")]
        public string NombreDato { get; set; } = string.Empty;

        /// <summary>
        /// Valor del dato
        /// </summary>
        [JsonProperty("Valor")]
        public string Valor { get; set; } = string.Empty;
    }

    /// <summary>
    /// Dimensiones del espacio detectadas en la fotografía
    /// </summary>
    public class DimensionesAnalisis
    {
        /// <summary>
        /// Ancho estimado del espacio en metros
        /// </summary>
        [JsonProperty("ancho")]
        public double? Ancho { get; set; }

        /// <summary>
        /// Largo estimado del espacio en metros
        /// </summary>
        [JsonProperty("largo")]
        public double? Largo { get; set; }

        /// <summary>
        /// Alto estimado del espacio en metros (altura de techo)
        /// </summary>
        [JsonProperty("alto")]
        public double? Alto { get; set; }

        /// <summary>
        /// Diámetro para objetos circulares en metros
        /// </summary>
        [JsonProperty("diametro")]
        public double? Diametro { get; set; }

        /// <summary>
        /// Observaciones sobre las dimensiones estimadas
        /// </summary>
        [JsonProperty("observaciones")]
        public string Observaciones { get; set; } = string.Empty;
    }
}
