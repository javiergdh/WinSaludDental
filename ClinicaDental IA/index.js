const express = require('express');
const Groq = require('groq-sdk');
const path = require('path');

const app = express();

// Configuración de Groq - Asegúrate de tener la variable de entorno en Railway
const groq = new Groq({ 
    apiKey: process.env.GROQ_API_KEY || 'TU_API_KEY_POR_SI_PRUEBAS_EN_LOCAL' 
}); 

app.use(express.json());

// 1. Servir archivos estáticos (CSS, Imágenes, JS)
app.use(express.static(path.join(__dirname, 'HTML')));

// 2. Ruta principal: Carga tu HTML específico
app.get('/', (req, res) => {
    res.sendFile(path.join(__dirname, 'HTML', 'Win Salud Dental.html')); 
});

// 3. Historial con las reglas de negocio de hoy miércoles 15 de abril
let historialConversacion = [
    {
        role: "system",
        content: `Eres Winni, la asistente virtual de "Win Salud Dental". 
        CONTEXTO ACTUAL: Hoy es miércoles 15 de abril de 2026.
        REGLAS CRÍTICAS:
        1. ESPECIALIDAD: Solo atendemos odontopediatría (niños y adolescentes).
        2. PRECIOS: La Limpieza infantil + Flúor tiene un coste fijo de 45€.
        3. RESTRICCIÓN DE CALENDARIO: No es posible añadir citas en fines de semana o festivos. 
           - Específicamente, este sábado 18 y domingo 19 de abril de 2026 la clínica está CERRADA.
        4. ACCIÓN: Si el usuario decide agendar, indica que debe usar el botón: [Abrir Formulario].
        5. ESTILO: Eres amable, profesional y directa.`
    }
];

// 4. Endpoint del Chat
app.post('/chat', async (req, res) => {
    try {
        const userMsg = req.body.mensaje;
        if (!userMsg) return res.status(400).json({ respuesta: "Mensaje vacío" });

        historialConversacion.push({ role: 'user', content: userMsg });

        const chatRes = await groq.chat.completions.create({
            messages: historialConversacion,
            model: "llama3-8b-8192", 
            temperature: 0.2 // Un poco más bajo para evitar que se invente horarios
        });

        const botReply = chatRes.choices[0].message.content;
        historialConversacion.push({ role: 'assistant', content: botReply });
        
        // Mantener el historial corto para no saturar la memoria (opcional)
        if (historialConversacion.length > 15) {
            historialConversacion = [historialConversacion[0], ...historialConversacion.slice(-10)];
        }

        res.json({ respuesta: botReply });
    } catch (error) {
        console.error("Error en Groq:", error);
        res.status(500).json({ 
            respuesta: "¡Ups! Winni está tomando un descanso técnico. Por favor, utiliza directamente el botón: [Abrir Formulario]" 
        });
    }
});

// 5. Configuración de Puerto para Railway
const PORT = process.env.PORT || 3000;
app.listen(PORT, () => {
    console.log(`🚀 Winni y Web de Clínica Dental operativas en puerto ${PORT}`);
});