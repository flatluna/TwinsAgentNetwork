# Casos de Uso del Cliente - Preguntas de Nutrición

## Descripción Simple

El cliente ahora puede hacer preguntas simples sobre su nutrición. El sistema:

1. **Obtiene todos los alimentos** que comió en el día
2. **Suma todos los nutrientes** (calorías, proteínas, grasas, etc.)
3. **Pregunta a la IA** (OpenAI) qué piensa sobre su alimentación
4. **Responde en lenguaje simple** sin términos complicados

**Ejemplo simple:**
- Cliente pregunta: "¿Comí bien hoy?"
- Sistema responde: "Sí, comiste bien. Buena cantidad de proteínas, un poco menos fibra."

---

## Casos de Uso Prácticos

### ?? Caso 1: Verificación Diaria de Salud Nutricional

**Escenario:**
Cliente quiere saber si su alimentación fue saludable.

**Pregunta:**
```
"¿Fue saludable lo que comí hoy?"
```

**Lo que el sistema hace:**
1. Obtiene: desayuno, almuerzo, cena, snacks del día
2. Suma: 2,150 calorías, 65g proteína, 280g carbohidratos, 72g grasas
3. Pregunta a OpenAI: "¿Es esto saludable?"
4. Responde: "Está bien, buena cantidad de proteínas, un poco alto en sodio"

**Respuesta típica:**
```
Tu consumo de hoy es equilibrado. Consumiste 2,150 calorías 
que es lo recomendado. Lo bueno: proteínas adecuadas.
Podría mejorar: un poco menos sodio sería ideal.
```

---

### ?? Caso 2: Análisis de Déficit de Nutrientes

**Escenario:**
Cliente se siente cansado y pregunta qué le falta.

**Pregunta:**
```
"¿Me faltó algo importante hoy? ¿Por qué me siento cansado?"
```

**Lo que el sistema hace:**
1. Obtiene los alimentos del día
2. Revisa: hierro, vitamina B12, vitamina C, magnesio
3. Identifica: poco hierro (12mg vs 18mg recomendado)
4. Pregunta a OpenAI: "¿Qué nutrientes faltan?"
5. Responde: "Te falta hierro, come más carne roja o espinacas"

**Respuesta típica:**
```
Te faltó un poco de hierro hoy (consumiste 12mg, se recomiendan 18mg).
Esto podría contribuir a tu cansancio. 
Come: carne roja, espinacas, lentejas o cereales enriquecidos.
```

---

### ?? Caso 3: Análisis de Macronutrientes

**Escenario:**
Cliente está haciendo ejercicio y quiere verificar su distribución.

**Pregunta:**
```
"¿Cuál fue mi proporción de proteínas, grasas y carbohidratos hoy?"
```

**Lo que el sistema hace:**
1. Obtiene: Proteínas (65g), Grasas (72g), Carbohidratos (280g)
2. Calcula porcentajes:
   - Proteínas: 22% (bueno para ejercicio)
   - Carbohidratos: 52% (normal)
   - Grasas: 30% (un poco alto)
3. Pregunta a OpenAI: "¿Es buena esta distribución?"
4. Responde: "Para ejercicio está bien, pero baja un poco las grasas"

**Respuesta típica:**
```
Tu distribución fue:
- Proteínas: 22% (perfecto para ejercicio)
- Carbohidratos: 52% (adecuado)
- Grasas: 30% (un poco alto)

Recomendación: Mantén las proteínas, baja grasas a 25%, 
sube carbohidratos a 55% para mejor rendimiento.
```

---

### ?? Caso 4: Control de Sodio para Hipertensión

**Escenario:**
Cliente tiene presión alta y necesita controlar el sodio.

**Pregunta:**
```
"¿Cuánto sodio consumí? ¿Es mucho para mi presión alta?"
```

**Lo que el sistema hace:**
1. Obtiene: Sodio total = 2,150mg (límite es 2,300mg)
2. Pregunta a OpenAI: "¿Es seguro este consumo de sodio?"
3. Responde: "Casi alcanzaste el límite, ten cuidado mañana"

**Respuesta típica:**
```
Consumiste 2,150mg de sodio, que está cerca del límite recomendado
de 2,300mg para personas con presión alta.

Hoy estuvo bien, pero mañana evita:
- Pan salado
- Embutidos
- Caldo preparado
- Salsas con sal
```

---

### ?? Caso 5: Preguntas Específicas (Continuación)

**Escenario:**
Cliente hace varias preguntas sobre el mismo día (conversación continua).

**Conversación:**

1. **Pregunta 1:** "¿Cómo estuvo mi alimentación?"
   - Respuesta: Está bien, equilibrada...
   
2. **Pregunta 2:** "¿Y el colesterol?"
   - Respuesta: Tu colesterol fue bajo, muy bien...
   
3. **Pregunta 3:** "¿Y si añado fruta mañana?"
   - Respuesta: Excelente idea, añade un plátano...

**Cómo funciona:**
- La IA recuerda la conversación anterior
- Usa el contexto de lo que ya se habló
- Puede responder preguntas de seguimiento

---

### ?? Caso 6: Comparación Entre Dos Días

**Escenario:**
Cliente quiere ver cómo fue hoy vs ayer.

**Pregunta:**
```
Hoy: "¿Cómo estuvo hoy?" (obtiene datos del día 15)
Ayer: "¿Y ayer?" (obtiene datos del día 14)
```

**Comparación:**
```
Día 14:
- Calorías: 1,900
- Proteína: 55g

Día 15:
- Calorías: 2,150
- Proteína: 65g

Hoy comiste 250 calorías más y más proteína. Está bien.
```

---

### ????? Caso 7: Validación de Plan Dietético

**Escenario:**
Cliente está siguiendo una dieta específica y quiere validar.

**Pregunta:**
```
"¿Hoy respetaré la dieta cetogénica? 
(Pregunta: ¿cuántos carbohidratos consumí?)"
```

**Lo que el sistema hace:**
1. Obtiene: Carbohidratos = 280g
2. Dieta cetogénica requiere: < 50g
3. Pregunta a OpenAI: "¿Esto rompe la dieta?"
4. Responde: "Sí, demasiados carbohidratos para cetogénica"

**Respuesta típica:**
```
Consumiste 280g de carbohidratos hoy.
Para una dieta cetogénica deberías tener máximo 50g.

Lo que te pasó de ceto:
- Pan en desayuno (45g)
- Pasta en almuerzo (120g)
- Jugo en cena (25g)

Mañana evita: pan, pasta, arroz, jugo. 
Come: huevos, queso, verduras, carne.
```

---

### ?? Caso 8: Recomendaciones Personalizadas

**Escenario:**
Cliente tiene objetivos específicos (perder peso, ganar músculo).

**Pregunta:**
```
"¿Qué necesito comer mañana para ganar músculo?"
```

**Lo que el sistema hace:**
1. Revisa datos de hoy
2. Identifica déficit de proteína
3. Pregunta a OpenAI: "¿Qué falta para ganar músculo?"
4. Responde: "Necesitas más proteína, come pollo y atún"

**Respuesta típica:**
```
Hoy consumiste 65g de proteína. Para ganar músculo necesitas 
1.6g por kg de peso corporal.

Si pesas 75kg, necesitas 120g diarios.

Te faltaron 55g hoy. Mañana añade:
- 1 pechuga de pollo (35g)
- Batido de proteína (25g)
- Queso fresco (15g)
```

---

## Flujo de Integración en la Aplicación

```
???????????????????????????????????????????????????????????
?                    APLICACIÓN CLIENTE                    ?
???????????????????????????????????????????????????????????
?                                                           ?
?  Pantalla: "Preguntas sobre mi nutrición"               ?
?  ????????????????????????????????????????????????????   ?
?  ? ¿Cómo estuvo mi alimentación hoy?               ?   ?
?  ? [Enviar pregunta]                               ?   ?
?  ????????????????????????????????????????????????????   ?
?                        ?                                  ?
?  SISTEMA:                                                ?
?  1. Obtiene alimentos del día (15/1/2025)              ?
?  2. Suma nutrientes: 2,150 cal, 65g proteína...        ?
?  3. Envía a OpenAI con la pregunta                     ?
?  4. Recibe respuesta: "Tu consumo fue equilibrado..."  ?
?                        ?                                  ?
?  Pantalla: Muestra respuesta                           ?
?  ????????????????????????????????????????????????????   ?
?  ? RESPUESTA:                                       ?   ?
?  ? Tu consumo fue equilibrado. 2,150 calorías,      ?   ?
?  ? buena proteína, un poco alto en sodio...        ?   ?
?  ?                                                  ?   ?
?  ? ¿Siguiente pregunta?                            ?   ?
?  ? [Escribir otra pregunta...]                     ?   ?
?  ????????????????????????????????????????????????????   ?
?                                                           ?
???????????????????????????????????????????????????????????
```

---

## Información Técnica (Para Desarrolladores)

### Parámetros que necesita el cliente enviar:

```json
{
    "twinId": "user-123",        // Su ID en el sistema
    "year": 2025,                // Año
    "month": 1,                  // Mes (1-12)
    "day": 15,                   // Día (1-31)
    "userQuestion": "¿Comí bien?" // Su pregunta
}
```

### Respuesta que recibe:

```json
{
    "success": true,
    "aiResponse": "Tu consumo fue...",        // Respuesta de IA
    "nutritionTotals": {                      // Totales calculados
        "caloriesPerTypicalServing": 2150,
        "proteinas": 65,
        "carbohidratos": 280,
        "grasas": 72,
        "sodio": 1850,
        "calcio": 800
        // ... más nutrientes
    },
    "foodEntriesCount": 4                     // Cuántos alimentos
}
```

---

## Preguntas Recomendadas para Clientes

### Fáciles (Empezar aquí):
- ¿Comí bien hoy?
- ¿Fue saludable?
- ¿Mucha o poca comida?
- ¿Qué nutrientes faltan?

### Intermedias:
- ¿Cuál fue mi proporción de proteína, grasas y carbohidratos?
- ¿Consumí demasiado sodio?
- ¿Mucho o poco hierro?
- ¿Fue equilibrado?

### Avanzadas:
- ¿Respeto mi dieta cetogénica?
- ¿Tengo suficiente proteína para ganar músculo?
- ¿Comí según mis calorías objetivo?
- ¿Faltan vitaminas específicas?

---

## Respuestas Típicas del Sistema

El sistema siempre responde:
- ? De forma **clara y simple**
- ? **Sin jerga técnica**
- ? **Basada en datos reales** del día
- ? Con **recomendaciones prácticas**
- ? **En español** siempre

**NO responde:**
- ? Con términos médicos complicados
- ? Con información genérica (responde sobre SU día)
- ? Sin fundamento en sus datos
- ? En otros idiomas

---

## Ejemplo Completo: Cliente Real

### Cliente: Ana, 35 años, ejecutiva

**Lunes 15 de enero:**

**Pregunta 1:**
```
Ana: "Hoy trabajé mucho y comí lo que pude. ¿Está bien?"
```

**Respuesta:**
```
Comiste 2,150 calorías, que es lo correcto. 
Pero notamos:
? Buena proteína (65g)
? Buen calcio (800mg)
? Muy alto en sodio (1,850mg)

Consejo: Menos sal mañana, come más verduras frescas.
```

**Pregunta 2:**
```
Ana: "¿Si como ensalada mañana ayuda?"
```

**Respuesta:**
```
Sí, mucho. La ensalada tiene:
- Bajo sodio
- Alta fibra
- Vitaminas

Consejo: Pídela sin sal, con limón.
Reduce tu sodio de 1,850 a 1,500mg fácilmente.
```

**Pregunta 3:**
```
Ana: "¿Qué más debo comer?"
```

**Respuesta:**
```
Hoy te faltó:
- Más fibra (tienes 28g, necesitas 35g)
- Hierro (tienes 15mg, se recomiendan 18mg)

Come: Una manzana (fibra) y un poco de carne roja (hierro).
```

---

## Conclusión

El cliente ahora puede:
1. **Hacer preguntas simples** sobre su nutrición
2. **Obtener respuestas personalizadas** basadas en lo que comió
3. **Recibir recomendaciones** prácticas y claras
4. **Mejorar su alimentación** paso a paso

**Todo sin necesidad de ser un experto en nutrición.**
