# ? SISTEMA LISTO PARA PROBAR

## ?? ¿Cómo Probar Ahora Mismo?

### **Opción 1: Visual Studio (MÁS FÁCIL)** ?

1. **Abrir Test Explorer:**
   - Menu ? Test ? Test Explorer
   - O presionar: `Ctrl + E, T`

2. **Ejecutar el test más completo:**
   - Buscar: `Test09_CompleteWorkflow_CreateSendReceive`
   - Click derecho ? **Run**
   - ?? Tiempo: ~12-18 segundos

3. **Ver resultados en vivo** ?

---

### **Opción 2: PowerShell Script** ??

```powershell
# Ejecutar el script interactivo
.\run-tests.ps1

# Seleccionar opción [4] para test completo
# o [5] para todos los tests
```

---

### **Opción 3: Línea de Comandos** ?

```bash
# Test completo individual
cd C:\TwinSourceServer\TwinAIFunctions\TwinAgentsNetworkTests
dotnet test --filter "Test09_CompleteWorkflow"

# TODOS los tests
dotnet test --filter "AgentTwinCommunicateTests"
```

---

## ?? ¿Qué Hacen las Pruebas?

### **Test09_CompleteWorkflow** (Recomendado para empezar)

Este test hace TODO el flujo completo:

```
? 1. Crea sesión con 2 usuarios
? 2. Usuario 1 envía: "Hola María, ¿cómo va el proyecto?"
? 3. Usuario 2 responde: "Muy bien Juan, estoy terminando..."
? 4. Usuario 1 pregunta a IA: "@assistant ¿Puedes sugerirme...?"
? 5. IA responde con sugerencia
? 6. Marca mensajes como leídos
```

**Resultado esperado:**
```
=================================================
?? TEST COMPLETO: Flujo de Trabajo Completo
=================================================

1?? Creando sesión...
   ? Sesión creada: abc-123-def

2?? Usuario 1 envía mensaje...
   ?? Juan ? María: Hola María, ¿cómo va el proyecto?
   ?? Enviado: 10:30:45

3?? Usuario 2 responde...
   ?? María ? Juan: Muy bien Juan, estoy terminando los reportes
   ?? Enviado: 10:30:46

4?? Usuario 1 pide ayuda al asistente...
   ?? Juan: @assistant ¿Puedes sugerirme...?
   ?? Asistente:
      [Respuesta inteligente de IA aquí]

5?? Marcando mensajes como leídos...
   ? 1 mensaje(s) marcado(s) como leído(s)

=================================================
? TEST COMPLETO EXITOSO
=================================================
```

---

## ?? Demo Rápido (1 minuto)

### En Visual Studio:

1. Abre **Test Explorer** (`Ctrl + E, T`)
2. Busca `Test09`
3. Click derecho ? **Run**
4. Espera 15 segundos
5. ¡Mira los resultados! ?

---

## ?? Todos los Tests Disponibles

| Test | Qué Prueba | Tiempo |
|------|------------|--------|
| **Test01** | Crear sesión básica | ~2 seg |
| **Test02** | Validación de errores | ~1 seg |
| **Test03** | Enviar mensaje simple | ~3 seg |
| **Test04** | Mensaje con IA (@assistant) | ~6 seg |
| **Test05** | Conversación 4 mensajes | ~10 seg |
| **Test06** | Chat grupal (3 personas) | ~8 seg |
| **Test07** | Marcar como leído | ~1 seg |
| **Test08** | Persistencia de thread | ~5 seg |
| **Test09** | **FLUJO COMPLETO** ? | ~15 seg |
| **Test10** | Performance (10 mensajes) | ~20 seg |

---

## ? Comandos Rápidos

```bash
# Test más completo (recomendado)
dotnet test --filter "Test09"

# 3 tests básicos
dotnet test --filter "Test01|Test03|Test07"

# TODOS los tests
dotnet test --filter "AgentTwinCommunicateTests"

# Con detalles
dotnet test --filter "Test09" --logger "console;verbosity=detailed"
```

---

## ?? Prerequisitos

**Antes de ejecutar, verificar:**

? Azure CLI autenticado:
```bash
az login
```

? Variables de entorno configuradas:
- `AZURE_OPENAI_ENDPOINT`
- `AZURE_OPENAI_MODEL_NAME`

? Build exitoso:
```bash
dotnet build
```

---

## ?? Archivo de Tests Creado

```
?? C:\TwinSourceServer\TwinAIFunctions\
   ??? TwinAgentsNetworkTests\
       ??? Agents\
           ??? AgentTwinCommunicateTests.cs  ? 10 tests aquí
```

---

## ?? Resultado Esperado

Si todo está bien configurado:

```
? Passed: 10
? Failed: 0
?? Skipped: 0
?? Total time: ~60 segundos (todos los tests)
```

---

## ?? Si Hay Errores

### Error: "Azure credentials not found"
```bash
az login
```

### Error: "AZURE_OPENAI_ENDPOINT not configured"
Verificar variables de entorno o `appsettings.json`

### Tests muy lentos
Normal - los tests con IA toman 5-8 seg por la llamada a OpenAI

---

## ?? ¿Qué Sigue?

Si los tests pasan:

1. **Ejecutar Azure Functions:**
   ```bash
   cd TwinAgentsNetwork
   func start
   ```

2. **Probar endpoints HTTP:**
   - POST `/api/communication/session/create`
   - POST `/api/communication/message/send`
   - GET `/api/communication/sessions/{twinId}`

3. **Integrar en tu frontend** (React, Vue, etc.)

---

## ?? Soporte

**Ver documentación completa:**
- `Documentation/TESTING_QUICK_GUIDE.md`
- `Documentation/AGENT_TWIN_COMMUNICATE_GUIDE.md`

**Archivos:**
- Tests: `../TwinAgentsNetworkTests/Agents/AgentTwinCommunicateTests.cs`
- Script: `run-tests.ps1`

---

**? Todo está listo. ¡Ejecuta los tests ahora!** ??
