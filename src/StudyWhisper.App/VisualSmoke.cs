using System.Globalization;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace StudyWhisper.App;

/// <summary>Actual native circle scenes with fixtures. No device, secrets, HTTP or system changes.</summary>
public static class VisualSmoke
{
    public static async Task RunAsync(string dir,Action<string,bool> check)
    {
        var history=0;var settings=0;var toggles=0;
        var orb=new OrbWindow(()=>history++,()=>settings++,()=>toggles++,()=>{},()=>{});
        var foreground=PassiveWindow.GetForegroundWindow();orb.Show();await Yield();
        long generation=1,sequence=0,item=1;
        var fixedLeft=orb.Left;var fixedTop=orb.Top;
        var scenes=new List<(string Label,BitmapSource Image)>();
        async Task Send(FlowKind kind,int queued=0){orb.Apply(new(kind,generation,++sequence,item,queued,true));await Task.Delay(175);await Yield();}
        void Capture(string name,string label)
        {
            var bitmap=UiSmoke.Capture(orb,Path.Combine(dir,"lens-"+name+".png"),192);scenes.Add((label,bitmap));
            check("Fixed circular aperture and anchor: "+name,orb.Width==72&&orb.Height==72&&orb.Left==fixedLeft&&orb.Top==fixedTop&&OutsideCircleTransparent(bitmap));
        }
        Capture("paused","Pausado");await Send(FlowKind.Enabled);Capture("idle","Escuta");
        // A 150 ms fade can finish between 15 Hz ticks; permit the final scheduled tick to stop.
        for(var n=0;n<8&&orb.AnimationRunning;n++)await Task.Delay(25);
        check("Idle circle has no animation timer",!orb.AnimationRunning);
        await Send(FlowKind.Candidate);Capture("filter","Filtro avaliando");check("Incomplete candidate shows only internal filter, never X",orb.Scene==LensScene.Filtering);
        await Send(FlowKind.SpeechDetected);check("Speech detection stays in filter until the segment ends",orb.Scene==LensScene.Filtering);
        await Send(FlowKind.LocalRejected);Capture("filter-rejected","Filtro descartou");check("Local rejection selects internal filter X",orb.Scene==LensScene.FilterRejected);
        await Send(FlowKind.Candidate);await Send(FlowKind.LocalApproved);Capture("filter-approved","Filtro aprovou");
        orb.Apply(new(FlowKind.SttStarted,generation,++sequence,item,FilterVerified:true));var opacity=orb.SceneOpacity;await Task.Delay(175);await Yield();
        check("Scene transition fades only the selected scene and settles in 150 ms",orb.Scene==LensScene.Transcribing&&orb.SceneOpacity==1&&(orb.EffectiveReduceMotion?opacity==1:opacity<1));Capture("whisper","Transcrição");
        await Send(FlowKind.TranscriptReady);Capture("transcript-ready","Texto recebido");await Send(FlowKind.JevStarted);Capture("jev","JEV avaliando");
        await Send(FlowKind.JevWaiting);Capture("wait","JEV aguardando");check("JEV wait is distinct from reject or approve",orb.Scene==LensScene.JevWaiting);
        item++;await Send(FlowKind.SttStarted);await Send(FlowKind.EmptyTranscript);Capture("stt-empty","STT vazio");
        item++;await Send(FlowKind.SttStarted);await Send(FlowKind.TranscriptReady);await Send(FlowKind.JevStarted);await Send(FlowKind.JevIgnored);Capture("jev-rejected","JEV ignorou");check("Comment selects JEV X without a response scene",orb.Scene==LensScene.JevIgnored&&orb.Flow.Cloud==StepMark.Pending);
        item++;await Send(FlowKind.SttStarted);await Send(FlowKind.TranscriptReady);await Send(FlowKind.JevStarted);await Send(FlowKind.JevApproved);Capture("jev-approved","JEV aprovou");await Send(FlowKind.AnswerStarted);
        // The displayed frequency values come from this measured synthetic PCM fixture.
        var spectrum=new Spectrum();AudioBands bands=default;for(var f=0;f<5;f++){var pcm=new short[320];for(var n=0;n<320;n++)pcm[n]=(short)(7000*Math.Sin(2*Math.PI*125*(f*320+n)/16000)+5000*Math.Sin(2*Math.PI*1000*(f*320+n)/16000)+3000*Math.Sin(2*Math.PI*4000*(f*320+n)/16000));bands=spectrum.Push(pcm)??bands;Array.Clear(pcm);}spectrum.Reset();orb.Spectrum(bands);await Task.Delay(80);await Yield();Capture("generating","Gerando texto");
        await Send(FlowKind.Queued,2);await Send(FlowKind.Overflow,2);Capture("queue","Fila durante resposta");check("Queue is an internal badge and tooltip, keeping the active scene",orb.Scene==LensScene.Generating&&orb.Flow.Queued==2&&orb.Flow.Incoming.Contains("Fila"));
        await Send(FlowKind.LocalRejected,2);check("New local rejection does not replace the current remote scene",orb.Scene==LensScene.Generating);
        var start=orb.RenderCount;await Task.Delay(1000);await Yield();check("Active decorative redraw is bounded to fifteen frames per second",orb.RenderCount-start<=17);
        orb.Configure(true);await Yield();Capture("reduced-motion","Movimento reduzido");check("Reduced motion stops animation and leaves one readable scene",orb.EffectiveReduceMotion&&!orb.AnimationRunning&&orb.Scene==LensScene.Generating);
        check("Accessible help explains the real stage and queue",AutomationProperties.GetHelpText(orb).Contains("processando")&&AutomationProperties.GetHelpText(orb).Contains("Fila"));
        foreach(var dpi in new[]{96,120,144,192}){var bitmap=UiSmoke.Capture(orb,Path.Combine(dir,$"lens-generating-{dpi}dpi.png"),dpi);check("Circular clipping at raster DPI "+dpi,bitmap.PixelWidth==(int)(72*dpi/96)&&bitmap.PixelHeight==(int)(72*dpi/96)&&OutsideCircleTransparent(bitmap));}
        await Send(FlowKind.Error);Capture("error","Erro");check("Error scene never displays a success check",orb.Scene==LensScene.Error);
        item++;await Send(FlowKind.SttStarted);await Send(FlowKind.TranscriptReady);await Send(FlowKind.JevStarted);await Send(FlowKind.JevApproved);await Send(FlowKind.AnswerStarted);await Send(FlowKind.ResponseReady);Capture("response","Resposta recebida");
        await Task.Delay(3200);await Yield();check("Completed scene returns to listening without changing circle position or size",orb.Scene==LensScene.Listening&&orb.Width==72&&orb.Height==72&&orb.Left==fixedLeft&&orb.Top==fixedTop&&!orb.AnimationRunning);
        start=orb.RenderCount;await Task.Delay(250);check("Idle/reduced motion does not continuously redraw",orb.RenderCount==start);
        generation++;await Send(FlowKind.Cancelled);Capture("cancelled","Cancelado");orb.Apply(new(FlowKind.AnswerStarted,generation-1,++sequence,item));check("Late results cannot revive cancelled internal animation",orb.Scene==LensScene.Paused&&!orb.AnimationRunning&&orb.Bands==AudioBands.Silent);
        check("All internal scenes preserve foreground focus and NOACTIVATE",PassiveWindow.GetForegroundWindow()==foreground&&orb.NoActivateVerified);
        var menu=((FrameworkElement)orb.Content).ContextMenu!;foreach(var index in new[]{0,1,2})((MenuItem)menu.Items[index]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        check("Circle menu keeps history, settings and monitoring accessible",history==1&&settings==1&&toggles==1);
        SaveComparison(scenes,Path.Combine(dir,"lens-scenes.png"));orb.Close();
    }
    private static bool OutsideCircleTransparent(BitmapSource bitmap)
    {
        var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);var scale=bitmap.PixelWidth/72.0;
        for(var y=0;y<bitmap.PixelHeight;y++)for(var x=0;x<bitmap.PixelWidth;x++){var dx=(x+.5)/scale-36;var dy=(y+.5)/scale-36;if(dx*dx+dy*dy>33*33&&pixels[(y*bitmap.PixelWidth+x)*4+3]!=0)return false;}return true;
    }
    private static void SaveComparison(List<(string Label,BitmapSource Image)> scenes,string file)
    {
        // Captions belong to this evidence sheet. They are not part of the application indicator.
        const int columns=4,cellWidth=184,cellHeight=160;var rows=(scenes.Count+columns-1)/columns;var width=columns*cellWidth+32;var height=rows*cellHeight+92;
        var visual=new DrawingVisual();using(var d=visual.RenderOpen())
        {
            d.DrawRectangle(Brushes.White,null,new Rect(0,0,width,height));
            FormattedText Label(string text,double size,Brush brush)=>new(text,CultureInfo.GetCultureInfo("pt-BR"),FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,1);
            d.DrawText(Label("StudyWhisper · um círculo, uma etapa por vez",20,Ui.Ink),new(24,16));d.DrawText(Label("Capturas nativas com fixtures sintéticas · legendas só nesta comparação",12,Ui.Muted),new(24,48));
            for(var n=0;n<scenes.Count;n++){var x=16+n%columns*cellWidth;var y=80+n/columns*cellHeight;d.DrawImage(scenes[n].Image,new Rect(x+38,y,108,108));var t=Label(scenes[n].Label,12,Ui.Ink);d.DrawText(t,new(x+(cellWidth-t.Width)/2,y+112));}
        }
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(file);encoder.Save(stream);
    }
    private static async Task Yield()=>await System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
}
