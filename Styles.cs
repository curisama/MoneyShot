// Money Shot — 전역 컨트롤 스타일
//
// UI를 코드로 짜다 보니 ScrollBar, Slider, ContextMenu 같은 기본 컨트롤이 시스템 기본
// 템플릿을 그대로 써서 밝은 회색 윈도 기본형으로 튄다. 여기서 한 번에 다크 테마로 덮는다.
// 템플릿은 XAML로 쓰는 편이 훨씬 짧아서 문자열로 정의하고 XamlReader로 읽는다.
using System;
using System.Windows;
using System.Windows.Markup;

namespace MoneyShot
{
    public static class Styles
    {
        public static void Apply(Application app)
        {
            try
            {
                var dict = (ResourceDictionary)XamlReader.Parse(Xaml);
                app.Resources.MergedDictionaries.Add(dict);
            }
            catch (Exception ex)
            {
                // 스타일이 없어도 앱은 돌아야 한다 — 기본 모양으로 떨어질 뿐.
                Log.W("전역 스타일 적용 실패: " + ex.Message);
            }
        }

        // 트레이 메뉴는 WPF가 아니라 WinForms라 위 스타일이 닿지 않는다. 색표를 직접 갈아끼운다.
        public class TrayColors : System.Windows.Forms.ProfessionalColorTable
        {
            static readonly System.Drawing.Color Bg = System.Drawing.Color.FromArgb(0x15, 0x15, 0x19);
            static readonly System.Drawing.Color Hi = System.Drawing.Color.FromArgb(0x26, 0x3A, 0x33);
            static readonly System.Drawing.Color Line = System.Drawing.Color.FromArgb(0x26, 0x26, 0x2C);

            public override System.Drawing.Color ToolStripDropDownBackground { get { return Bg; } }
            public override System.Drawing.Color MenuBorder { get { return Line; } }
            public override System.Drawing.Color MenuItemBorder { get { return Hi; } }
            public override System.Drawing.Color MenuItemSelected { get { return Hi; } }
            public override System.Drawing.Color MenuItemSelectedGradientBegin { get { return Hi; } }
            public override System.Drawing.Color MenuItemSelectedGradientEnd { get { return Hi; } }
            public override System.Drawing.Color MenuItemPressedGradientBegin { get { return Hi; } }
            public override System.Drawing.Color MenuItemPressedGradientEnd { get { return Hi; } }
            public override System.Drawing.Color ImageMarginGradientBegin { get { return Bg; } }
            public override System.Drawing.Color ImageMarginGradientMiddle { get { return Bg; } }
            public override System.Drawing.Color ImageMarginGradientEnd { get { return Bg; } }
            public override System.Drawing.Color SeparatorDark { get { return Line; } }
            public override System.Drawing.Color SeparatorLight { get { return Line; } }
        }

        public static void ApplyTray(System.Windows.Forms.ContextMenuStrip m)
        {
            m.RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional;
            m.Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new TrayColors());
            m.BackColor = System.Drawing.Color.FromArgb(0x15, 0x15, 0x19);
            m.ForeColor = System.Drawing.Color.FromArgb(0xF4, 0xF4, 0xF5);
            m.ShowImageMargin = false;
            m.Font = new System.Drawing.Font("Segoe UI", 9f);
            foreach (System.Windows.Forms.ToolStripItem it in m.Items)
            {
                it.ForeColor = System.Drawing.Color.FromArgb(0xF4, 0xF4, 0xF5);
                it.BackColor = System.Drawing.Color.FromArgb(0x15, 0x15, 0x19);
            }
        }

        const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                    xmlns:s='clr-namespace:System;assembly=mscorlib'>

  <Color x:Key='cBg'>#FF0B0B0F</Color>
  <Color x:Key='cCard'>#FF151519</Color>
  <Color x:Key='cAccent'>#FF34D399</Color>
  <SolidColorBrush x:Key='bBg' Color='#FF0B0B0F'/>
  <SolidColorBrush x:Key='bCard' Color='#FF151519'/>
  <SolidColorBrush x:Key='bCardHi' Color='#FF1E1E24'/>
  <SolidColorBrush x:Key='bBorder' Color='#FF26262C'/>
  <SolidColorBrush x:Key='bText' Color='#FFF4F4F5'/>
  <SolidColorBrush x:Key='bMuted' Color='#FF8A8A94'/>
  <SolidColorBrush x:Key='bAccent' Color='#FF34D399'/>
  <SolidColorBrush x:Key='bThumb' Color='#FF3A3A45'/>
  <SolidColorBrush x:Key='bThumbHi' Color='#FF56566A'/>

  <!-- ===== 스크롤바 =====
       윈도 기본 스크롤바는 화살표 버튼과 회색 홈통이 있어 다크 UI에서 눈에 띄게 튄다.
       버튼을 없애고 얇은 알약 손잡이만 남긴다. 홈통은 마우스를 올렸을 때만 옅게 드러난다. -->
  <Style x:Key='sbThumb' TargetType='{x:Type Thumb}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type Thumb}'>
          <Border x:Name='t' CornerRadius='4' Background='{StaticResource bThumb}' Margin='3'/>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='t' Property='Background' Value='{StaticResource bThumbHi}'/>
            </Trigger>
            <Trigger Property='IsDragging' Value='True'>
              <Setter TargetName='t' Property='Background' Value='{StaticResource bAccent}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 페이지 단위 이동 영역. 보이지는 않지만 클릭은 받아야 한다. -->
  <Style x:Key='sbPage' TargetType='{x:Type RepeatButton}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type RepeatButton}'>
          <Border Background='Transparent'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <ControlTemplate x:Key='sbVertical' TargetType='{x:Type ScrollBar}'>
    <Grid x:Name='root' Background='Transparent' Width='11'>
      <Border x:Name='trough' CornerRadius='5' Background='#00FFFFFF' Margin='3,0,3,0'/>
      <Track x:Name='PART_Track' IsDirectionReversed='True'>
        <Track.Thumb>
          <Thumb Style='{StaticResource sbThumb}'/>
        </Track.Thumb>
        <Track.IncreaseRepeatButton>
          <RepeatButton Style='{StaticResource sbPage}' Command='ScrollBar.PageDownCommand'/>
        </Track.IncreaseRepeatButton>
        <Track.DecreaseRepeatButton>
          <RepeatButton Style='{StaticResource sbPage}' Command='ScrollBar.PageUpCommand'/>
        </Track.DecreaseRepeatButton>
      </Track>
    </Grid>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'>
        <Setter TargetName='trough' Property='Background' Value='#14FFFFFF'/>
      </Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>

  <ControlTemplate x:Key='sbHorizontal' TargetType='{x:Type ScrollBar}'>
    <Grid x:Name='root' Background='Transparent' Height='11'>
      <Border x:Name='trough' CornerRadius='5' Background='#00FFFFFF' Margin='0,3,0,3'/>
      <Track x:Name='PART_Track' IsDirectionReversed='False' Orientation='Horizontal'>
        <Track.Thumb>
          <Thumb Style='{StaticResource sbThumb}'/>
        </Track.Thumb>
        <Track.IncreaseRepeatButton>
          <RepeatButton Style='{StaticResource sbPage}' Command='ScrollBar.PageRightCommand'/>
        </Track.IncreaseRepeatButton>
        <Track.DecreaseRepeatButton>
          <RepeatButton Style='{StaticResource sbPage}' Command='ScrollBar.PageLeftCommand'/>
        </Track.DecreaseRepeatButton>
      </Track>
    </Grid>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'>
        <Setter TargetName='trough' Property='Background' Value='#14FFFFFF'/>
      </Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>

  <Style TargetType='{x:Type ScrollBar}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='11'/>
    <Setter Property='MinWidth' Value='11'/>
    <Setter Property='Template' Value='{StaticResource sbVertical}'/>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='11'/>
        <Setter Property='MinHeight' Value='11'/>
        <Setter Property='Template' Value='{StaticResource sbHorizontal}'/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType='{x:Type ScrollViewer}'>
    <Setter Property='Background' Value='Transparent'/>
  </Style>

  <!-- ===== 슬라이더 =====
       기본 슬라이더는 파란 계열 크롬을 쓴다. 얇은 홈통 + 채워진 구간 + 둥근 손잡이로 바꾼다. -->
  <Style TargetType='{x:Type Slider}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='MinHeight' Value='20'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type Slider}'>
          <Grid Background='Transparent' VerticalAlignment='Center'>
            <Border Height='4' CornerRadius='2' Background='#22FFFFFF' VerticalAlignment='Center'/>
            <!-- 채움 너비는 트랙 왼쪽 페이지 버튼의 실제 너비를 그대로 따라간다 -->
            <Border x:Name='fill' Height='4' CornerRadius='2' Background='{StaticResource bAccent}'
                    HorizontalAlignment='Left' VerticalAlignment='Center'
                    Width='{Binding ActualWidth, ElementName=dec}'/>
            <Track x:Name='PART_Track'>
              <Track.Thumb>
                <Thumb x:Name='th' Width='14' Height='14'>
                  <Thumb.Template>
                    <ControlTemplate TargetType='{x:Type Thumb}'>
                      <Grid>
                        <Ellipse x:Name='glow' Width='24' Height='24' Fill='#2234D399' Opacity='0'/>
                        <Ellipse Width='14' Height='14' Fill='White'/>
                      </Grid>
                      <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'>
                          <Setter TargetName='glow' Property='Opacity' Value='1'/>
                        </Trigger>
                        <Trigger Property='IsDragging' Value='True'>
                          <Setter TargetName='glow' Property='Opacity' Value='1'/>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
              <Track.IncreaseRepeatButton>
                <RepeatButton Style='{StaticResource sbPage}' Command='Slider.IncreaseLarge'/>
              </Track.IncreaseRepeatButton>
              <Track.DecreaseRepeatButton>
                <RepeatButton x:Name='dec' Style='{StaticResource sbPage}' Command='Slider.DecreaseLarge'/>
              </Track.DecreaseRepeatButton>
            </Track>
          </Grid>
          <ControlTemplate.Triggers>
            <!-- 채워진 구간의 너비는 왼쪽 페이지 버튼의 실제 너비를 그대로 따라간다 -->
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== 컨텍스트 메뉴 ===== -->
  <Style TargetType='{x:Type ContextMenu}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ContextMenu}'>
          <Border Background='#F2151519' BorderBrush='{StaticResource bBorder}' BorderThickness='1'
                  CornerRadius='10' Padding='5'>
            <Border.Effect>
              <DropShadowEffect BlurRadius='18' ShadowDepth='4' Direction='270' Opacity='0.5' Color='Black'/>
            </Border.Effect>
            <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='{x:Type MenuItem}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{StaticResource bText}'/>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type MenuItem}'>
          <Border x:Name='b' CornerRadius='7' Padding='11,6,14,7' Background='Transparent'>
            <ContentPresenter ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='b' Property='Background' Value='#2634D399'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Foreground' Value='{StaticResource bMuted}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='{x:Type Separator}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type Separator}'>
          <Border Height='1' Margin='9,5,9,5' Background='#18FFFFFF'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== 입력·기타 ===== -->
  <Style TargetType='{x:Type TextBox}'>
    <Setter Property='Foreground' Value='{StaticResource bText}'/>
    <Setter Property='CaretBrush' Value='{StaticResource bAccent}'/>
    <Setter Property='SelectionBrush' Value='{StaticResource bAccent}'/>
    <Setter Property='Background' Value='#CC151519'/>
    <Setter Property='BorderBrush' Value='{StaticResource bBorder}'/>
    <Setter Property='BorderThickness' Value='1'/>
  </Style>

  <Style TargetType='{x:Type ToolTip}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ToolTip}'>
          <Border Background='#F20B0B0F' BorderBrush='{StaticResource bBorder}' BorderThickness='1'
                  CornerRadius='7' Padding='9,5,9,6'>
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Setter Property='Foreground' Value='{StaticResource bText}'/>
    <Setter Property='FontSize' Value='11.5'/>
  </Style>

  <Style TargetType='{x:Type ProgressBar}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ProgressBar}'>
          <Border CornerRadius='3' Background='#22FFFFFF' ClipToBounds='True'>
            <Grid>
              <Rectangle x:Name='PART_Track'/>
              <Rectangle x:Name='PART_Indicator' HorizontalAlignment='Left' Fill='{StaticResource bAccent}'
                         RadiusX='3' RadiusY='3'/>
            </Grid>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";
    }
}
