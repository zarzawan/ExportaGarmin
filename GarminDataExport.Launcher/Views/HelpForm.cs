namespace GarminDataExport.Launcher.Views;

internal sealed class HelpForm : Form
{
    public HelpForm()
    {
        Text = "Guía sencilla";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(680, 500);
        Size = new Size(760, 570);
        Font = new Font("Segoe UI", 10F);

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
        tabs.TabPages.Add(CreatePage(
            "Primeros pasos",
            """
            1. Elige tu perfil arriba. Cada perfil guarda por separado su sesión, caché, contexto, diario y exportaciones.

            2. Pulsa «Iniciar sesión» una sola vez. Se abrirá una ventana negra. Escribe allí tu usuario, contraseña y código MFA. Nunca los escribas en un chat.

            3. Abre «Mi carrera» y añade la prueba que preparas. Solo tienes que hacerlo una vez y puedes corregirlo cuando quieras.

            4. Deja seleccionado «Texto con JSON (.txt)», el formato recomendado para IA y el más rápido de crear. Conserva toda la estructura JSON y las series detalladas. Excel queda disponible de forma opcional para revisar los datos como tablas.

            5. Usa «Revisión recomendada» para crear el archivo habitual. Después súbelo manualmente a la IA que prefieras y pulsa «Copiar pregunta para la IA».

            El panel «Progreso de la exportación» permanece visible. Si algo falla, revisa allí el último paso; no compartas el registro sin comprobarlo.
            """));
        tabs.TabPages.Add(CreatePage(
            "Cada día",
            """
            Sincroniza el reloj con Garmin Connect.

            Después de una sesión importante, completa la autoevaluación de Garmin. Si quieres aportar información que el reloj no conoce, abre «Mi diario» y escribe un comentario. Se incluirá automáticamente en el informe para la IA. La actividad aparece con el día de la semana, fecha, nombre, distancia y duración. Si falta una sesión reciente, pulsa «Actualizar actividades». Para editar, elige una anotación guardada y pulsa «Abrir anotación»; también puedes hacer doble clic en la tabla.

            No hace falta exportar todos los días. El diario es opcional y conviene reservarlo para entrenamientos importantes.
            """));
        tabs.TabPages.Add(CreatePage(
            "Cada semana",
            """
            1. Selecciona «Revisión recomendada».
            2. Mantén 16 semanas para maratón o 12 para media maratón.
            3. Pulsa «Crear archivo para la IA».
            4. Sube el archivo nuevo a ChatGPT y sustituye el anterior.
            5. Copia la pregunta preparada y pégala en la conversación. La pregunta hace que la IA revise primero la calidad de los datos y compare únicamente semanas completas equivalentes.

            El archivo es autocontenido: no tienes que combinar muchas actualizaciones antiguas.
            """));
        tabs.TabPages.Add(CreatePage(
            "Cada mes",
            """
            Haz una revisión más estratégica con el mismo archivo semanal:

            • evolución de volumen, tirada larga y constancia;
            • respuesta al entrenamiento y recuperación;
            • cercanía de la carrera y prioridades del próximo bloque;
            • calidad y ausencias de los datos.

            La IA ayuda a interpretar tendencias, pero no sustituye a un entrenador o profesional sanitario.
            """));
        tabs.TabPages.Add(CreatePage(
            "Privacidad",
            """
            Las exportaciones contienen datos de salud y entrenamiento. Guárdalas como documentos personales y revisa el archivo antes de compartirlo.

            La privacidad es automática: retira identidad e identificadores personales, pero conserva títulos, coordenadas exactas, tracks, altitud, desnivel, vueltas y GAP/RAP. Las coordenadas pueden revelar dónde entrenas: revisa el archivo antes de compartirlo.

            La caché local puede conservar respuestas originales de Garmin y por eso nunca debe subirse a Git.

            El nombre, fabricante y modelo de las zapatillas, bicicletas u otro equipo asociado sí aparecen para ayudar a interpretar cada actividad. Los identificadores reales se eliminan. Revisa los nombres personalizados si has escrito información personal dentro de ellos.

            Las sesiones se guardan fuera del proyecto. El lanzador no guarda correo, contraseña, MFA ni tokens dentro de sus preferencias. Un perfil existente usa ~/.garminconnect sin mover ni copiar su contenido.

            Los resultados se guardan dentro de tu carpeta Documentos. Si Windows tiene Documentos sincronizado con OneDrive u otro servicio, también podría sincronizar esos archivos. Comprueba tu configuración si quieres que permanezcan únicamente en este PC.

            Si varias personas usan el mismo PC, lo más seguro es que cada una tenga una cuenta de Windows diferente.
            """));
        tabs.TabPages.Add(CreatePage(
            "Actualizar",
            """
            ExportaGarmin comprueba una vez al día si existe una versión nueva. También puedes pulsar «Comprobar versión» en la parte superior.

            Si hay una actualización, abre la página oficial, descarga el ZIP, extráelo en una carpeta nueva y abre el nuevo ExportaGarmin.exe. La descarga y la instalación nunca se hacen solas.

            Tus perfiles, sesión de Garmin, anotaciones y caché se guardan fuera de la carpeta del programa. También se conservan los informes de Documentos. Por eso puedes cambiar de versión sin copiar ni configurar de nuevo esos datos.
            """));

        var closeButton = new Button
        {
            Text = "Cerrar",
            AutoSize = true,
            Padding = new Padding(14, 5, 14, 5),
            Anchor = AnchorStyles.Right,
            DialogResult = DialogResult.OK,
        };
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10, 8, 16, 8),
        };
        bottom.Controls.Add(closeButton);

        Controls.Add(tabs);
        Controls.Add(bottom);
        AcceptButton = closeButton;
    }

    private static TabPage CreatePage(string title, string content)
    {
        var page = new TabPage(title) { Padding = new Padding(18) };
        page.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window,
            Font = new Font("Segoe UI", 11F),
            Text = content.Replace("\n", Environment.NewLine, StringComparison.Ordinal),
            ScrollBars = ScrollBars.Vertical,
        });
        return page;
    }
}
