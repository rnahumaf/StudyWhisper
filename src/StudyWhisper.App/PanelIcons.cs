using System.Globalization;
using System.Windows.Automation;
using System.Xml.Linq;
namespace StudyWhisper.App;

// Local Lucide SVGs (ISC/MIT), rendered as WPF vectors without a browser or network.
internal static class PanelIcons
{
    private static readonly Dictionary<string,DrawingImage> images=new();
    public static Image Icon(string name)
    {
        if(!images.TryGetValue(name,out var image))
        {
            using var stream=typeof(PanelIcons).Assembly.GetManifestResourceStream($"StudyWhisper.Icons.{name}.svg")??throw new InvalidOperationException("Missing icon: "+name);
            var svg=XDocument.Load(stream).Root!;
            var drawing=new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,24,24))));
            var pen=new Pen(Ui.Ink,2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
            foreach(var e in svg.Elements())
            {
                double N(string key,double fallback=0)=>double.TryParse((string?)e.Attribute(key),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)?value:fallback;
                Geometry geometry=e.Name.LocalName switch
                {
                    "path"=>Geometry.Parse((string)e.Attribute("d")!),
                    "circle"=>new EllipseGeometry(new Point(N("cx"),N("cy")),N("r"),N("r")),
                    "rect"=>new RectangleGeometry(new Rect(N("x"),N("y"),N("width"),N("height")),N("rx"),N("ry",N("rx"))),
                    _=>throw new InvalidOperationException("Unsupported local SVG element")
                };
                drawing.Children.Add(new GeometryDrawing((string?)e.Attribute("fill")=="currentColor"?Ui.Ink:null,pen,geometry));
            }
            image=new DrawingImage(drawing);image.Freeze();images.Add(name,image);
        }
        return new Image{Source=image,Width=18,Height=18,Stretch=Stretch.Uniform};
    }
    public static Button Button(string icon,string label,Action action)
    {
        var button=Ui.Button(label,action);button.Width=34;button.Height=34;button.Padding=new Thickness(7);button.Margin=new Thickness(2);button.Cursor=System.Windows.Input.Cursors.Hand;
        Set(button,icon,label);
        button.Background=Brushes.Transparent;button.BorderThickness=new Thickness(0);
        var surface=new FrameworkElementFactory(typeof(Border),"Surface");surface.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));surface.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);surface.AppendChild(content);
        var template=new ControlTemplate(typeof(Button)){VisualTree=surface};
        var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromRgb(237,243,246)),"Surface"));template.Triggers.Add(hover);
        var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromRgb(221,235,241)),"Surface"));template.Triggers.Add(focus);button.Template=template;
        var style=new Style(typeof(Button));
        var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,.4));style.Triggers.Add(disabled);button.Style=style;
        return button;
    }
    public static void StyleNavigation(Window window)
    {
        foreach(var type in new[]{typeof(ListBoxItem),typeof(TabItem)})
        {
            var style=new Style(type);style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,6,9,6)));style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0,0,0,2)));style.Setters.Add(new Setter(Control.BorderBrushProperty,Brushes.Transparent));style.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));
            var edge=new FrameworkElementFactory(typeof(Border));edge.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));edge.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));edge.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Control.BorderThicknessProperty));edge.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));
            var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);content.SetValue(ContentPresenter.ContentSourceProperty,type==typeof(TabItem)?"Header":"Content");edge.AppendChild(content);
            var template=new ControlTemplate(type){VisualTree=edge};
            var selected=new Trigger{Property=type==typeof(TabItem)?TabItem.IsSelectedProperty:ListBoxItem.IsSelectedProperty,Value=true};selected.Setters.Add(new Setter(Control.BorderBrushProperty,Ui.Accent));template.Triggers.Add(selected);
            var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(242,246,248))));template.Triggers.Add(hover);
            var focus=new Trigger{Property=UIElement.IsKeyboardFocusWithinProperty,Value=true};focus.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(230,240,245))));template.Triggers.Add(focus);
            style.Setters.Add(new Setter(Control.TemplateProperty,template));window.Resources[type]=style;
        }
    }
    public static void Set(ContentControl button,string icon,string label,string? caption=null)
    {
        var key=(icon,caption);
        if(!Equals(button.Tag,key))
        {
            var image=Icon(icon);
            if(caption is null)button.Content=image;
            else
            {
                var content=new StackPanel{Orientation=Orientation.Horizontal};
                content.Children.Add(image);
                content.Children.Add(new TextBlock{Text=caption,Margin=new Thickness(6,0,0,0),VerticalAlignment=VerticalAlignment.Center});
                button.Content=content;
            }
            button.Tag=key;
        }
        button.ToolTip=label;AutomationProperties.SetName(button,label);
    }
}
