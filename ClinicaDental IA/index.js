const express = require('express');
const { Ollama } = require('ollama');
const path = require('path');

const app = express();
const ollama = new Ollama();
app.use(express.json());

// 1. Servir archivos desde la raíz (para vídeos, etc.)
app.use(express.static(path.join(__dirname))); 

// 2. Servir archivos TAMBIÉN desde la carpeta HTML (esto ayuda a encontrar los .html)
app.use(express.static(path.join(__dirname, 'HTML')));

let historialConversacion = [];

app.get('/', (req, res) => {
    // Intentamos enviarlo con la ruta absoluta completa
    res.sendFile(path.join(__dirname, 'HTML', 'Win Salud Dental.html')); 
});

app.post('/chat', async (req, res) => {
    try {
        const userMsg = req.body.mensaje;
        historialConversacion.push({ role: 'user', content: userMsg });

        const chatRes = await ollama.chat({
            model: 'asistente-dental',
            messages: historialConversacion,
            options: { temperature: 0.1 } 
        });

        const botReply = chatRes.message.content;
        historialConversacion.push({ role: 'assistant', content: botReply });
        res.json({ respuesta: botReply });
    } catch (error) {
        res.status(500).json({ respuesta: "Error. [Abrir Formulario]" });
    }
});

app.listen(3000, () => console.log('🚀 Web en http://localhost:3000'));