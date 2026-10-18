// Couleurs et styles communs a ZikTray et a son installateur.
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

static class Theme
{
    public static readonly Color AccentColor = Color.FromRgb(0xFF, 0x6A, 0x1A);
    public static readonly Brush Accent = Frozen(AccentColor);
    public static readonly Brush Surface = Frozen(Color.FromRgb(0x1C, 0x1C, 0x1E));
    public static readonly Brush Raised = Frozen(Color.FromRgb(0x2A, 0x2A, 0x2C));
    public static readonly Brush Line = Frozen(Color.FromRgb(0x3A, 0x3A, 0x3C));
    public static readonly Brush Text = Frozen(Color.FromRgb(0xF2, 0xF2, 0xF7));
    public static readonly Brush Muted = Frozen(Color.FromRgb(0x8E, 0x8E, 0x93));
    public static readonly Brush Good = Frozen(Color.FromRgb(0x30, 0xD1, 0x58));
    public static readonly Brush Bad = Frozen(Color.FromRgb(0xFF, 0x45, 0x3A));
    public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI");

    public static Brush Frozen(Color color)
    {
        SolidColorBrush brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <SolidColorBrush x:Key='Accent' Color='#FF6A1A'/>

  <Style x:Key='Switch' TargetType='CheckBox'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <Grid Background='Transparent'>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width='*'/>
              <ColumnDefinition Width='Auto'/>
            </Grid.ColumnDefinitions>
            <ContentPresenter VerticalAlignment='Center'/>
            <Border x:Name='track' Grid.Column='1' Width='42' Height='24' CornerRadius='12' Background='#3A3A3C'>
              <Ellipse x:Name='knob' Width='18' Height='18' Fill='White' HorizontalAlignment='Left' Margin='3,0'/>
            </Border>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='track' Property='Background' Value='{StaticResource Accent}'/>
              <Setter TargetName='knob' Property='HorizontalAlignment' Value='Right'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Segment' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='#B4B4BA'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Border x:Name='back' CornerRadius='7' Background='Transparent' Padding='2,7'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='back' Property='Background' Value='#38383A'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='back' Property='Background' Value='{StaticResource Accent}'/>
              <Setter Property='Foreground' Value='White'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Button' TargetType='Button'>
    <Setter Property='Foreground' Value='White'/>
    <Setter Property='Background' Value='#3A3A3C'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='back' CornerRadius='8' Background='{TemplateBinding Background}' Padding='20,8'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='back' Property='Opacity' Value='0.85'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='back' Property='Opacity' Value='0.4'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Band' TargetType='Slider'>
    <Setter Property='Orientation' Value='Vertical'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Slider'>
          <Grid Width='34' Background='Transparent'>
            <Border Width='4' CornerRadius='2' Background='#3A3A3C' HorizontalAlignment='Center'/>
            <Border Width='16' Height='2' Background='#58585C' VerticalAlignment='Center'/>
            <Track x:Name='PART_Track'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Command='Slider.DecreaseLarge' Opacity='0' Focusable='False'/>
              </Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command='Slider.IncreaseLarge' Opacity='0' Focusable='False'/>
              </Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb Cursor='Hand'>
                  <Thumb.Template>
                    <ControlTemplate TargetType='Thumb'>
                      <Ellipse Width='18' Height='18' Fill='#FF6A1A' Stroke='White' StrokeThickness='2'/>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

    public static readonly ResourceDictionary Styles = (ResourceDictionary)XamlReader.Parse(Xaml);

    public static Style Style(string key)
    {
        return (Style)Styles[key];
    }
}

