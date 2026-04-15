const express = require('express');
const Groq = require('groq-sdk'); // Cambiamos Ollama por Groq
const path = require('path');

const app = express();
// Sustituye 'TU_API_KEY_AQUI' por tu llave de groq.com (es gratis)
const groq = new Groq({ apiKey: 'GROQ_API_KEY' }); 

app.use(express.json());
app.use(express.static(path.join(__dirname))); 
app.use(express.static(path.join(__dirname, 'HTML')));

let historialConversacion = [
    {
        role: "system",
        content: `Eres Winni, asistente de Win Salud Dental. 
        Hoy es miércoles 15 de abril de 2026.
        REGLAS:
        1. Solo odontopediatría (niños).
        2. Limpieza infantil + Flúor: 45€.
        3. No agendamos citas fines de semana ni festivos (sábado 18 y domingo 19 prohibidos).
        4. Si quieren cita, usa siempre: [Abrir Formulario].`
    }
];

app.get('/', (req, res) => {
    res.sendFile(path.join(__dirname, 'HTML', 'Win Salud Dental.html')); 
});

app.post('/chat', async (req, res) => {
    try {
        const userMsg = req.body.mensaje;
        historialConversacion.push({ role: 'user', content: userMsg });

        const chatRes = await groq.chat.completions.create({
            messages: historialConversacion,
            model: "llama3-8b-8192", // Modelo potente y gratuito
            temperature: 0.1
        });

        const botReply = chatRes.choices[0].message.content;
        historialConversacion.push({ role: 'assistant', content: botReply });
        res.json({ respuesta: botReply });
    } catch (error) {
        console.error(error);
        res.status(500).json({ respuesta: "Lo siento, tengo un problema técnico. Por favor, usa el formulario: [Abrir Formulario]" });
    }
});

// Railway asigna el puerto automáticamente con process.env.PORT
const PORT = process.env.PORT || 3000;
app.listen(PORT, () => console.log(`🚀 Winni operativa en puerto ${PORT}`));