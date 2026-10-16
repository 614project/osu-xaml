# OsuXaml: osu!framework 위의 컴파일형 XAML

osu!framework 타입 이름을 그대로 쓰는 XAML을 빌드 시점에 C#으로 바꾸는 소스 생성기와, 그것으로 만든 할 일(Todo) 앱입니다. 구현 가능성 검증용 프로토타입입니다.

![demo](docs/todo-demo.gif)

## 구성

| 경로 | 내용 |
| --- | --- |
| `src/OsuXaml.Generator` | Roslyn Incremental Source Generator. `*.xaml`을 읽어 `InitializeComponent()`를 생성 |
| `src/OsuXaml` | 생성된 코드가 호출하는 런타임 (`XamlBinding`, `BindingScope`). 리플렉션 없음 |
| `src/OsuXaml/Controls` | WPF식 컨트롤: Grid, StackPanel, DockPanel, WrapPanel, Canvas, UniformGrid, Border, ScrollViewer, TextBlock, Button, CheckBox, TextBox, Slider, ProgressBar, Separator |
| `samples/TodoApp` | XAML 한 파일 + 뷰모델로 만든 할 일 앱 (osu!framework 기본 컨트롤 사용) |
| `samples/Gallery` | WPF식 컨트롤 갤러리 |
| `tests/OsuXaml.Tests` | 생성기 테스트 10개, 레이아웃 기하 테스트 6개, 헤드리스 앱 테스트 4개 |

## 실행

```bash
dotnet run --project samples/TodoApp     # 할 일 앱 (.NET 10 SDK)
dotnet run --project samples/Gallery     # WPF식 컨트롤 갤러리
dotnet test tests/OsuXaml.Tests           # 테스트
```

생성된 C#을 보고 싶으면 `dotnet build samples/TodoApp -p:EmitCompilerGeneratedFiles=true` 후 `obj/Debug/net10.0/generated/`를 확인하세요.

## 동작 방식

1. 앱 프로젝트가 `<AdditionalFiles Include="**\*.xaml" />`로 XAML을 생성기에 넘깁니다.
2. 생성기는 요소 이름을 osu!framework 타입으로, 속성을 프로퍼티로 Roslyn 심볼에서 찾습니다. 못 찾으면 XAML 줄 번호를 가리키는 빌드 오류(OX0001~OX0011)를 냅니다.
3. `x:Class`의 partial 클래스에 `InitializeComponent(뷰모델)`과 `x:Name` 필드를 생성합니다. 생성 코드는 사람이 손으로 쓴 osu!framework 코드와 거의 같습니다.
4. `{Bind}`는 경로와 타입을 빌드 시점에 검사한 뒤 Bindable 연결 코드로 바뀝니다. 런타임 비용은 손으로 쓴 `BindValueChanged`와 같습니다.

```csharp
public partial class MainView : CompositeDrawable
{
    [BackgroundDependencyLoader]
    private void load(TodoViewModel viewModel) => InitializeComponent(viewModel);

    private static float DoneToAlpha(bool done) => done ? 0.35f : 1f;
}
```

## 문법

```xml
<CompositeDrawable xmlns="osu-xaml" xmlns:x="osu-xaml:x" xmlns:local="clr-namespace:TodoApp"
                   x:Class="TodoApp.MainView" x:DataType="local:TodoViewModel" RelativeSizeAxes="Both">
  <FillFlowContainer Direction="Vertical" Spacing="0,14" Margin="0,48,0,0" ItemsSource="{Bind Items}">
    <FillFlowContainer.ItemTemplate>
      <Container RelativeSizeAxes="X" Height="44">
        <BasicCheckbox Current="{Bind Done}" />
        <SpriteText Text="{Bind Text}" Alpha="{Bind Done, Converter=DoneToAlpha, Animate=250}" />
        <BasicButton Text="x" Action="{Bind RemoveItem, Source=Root}" />
      </Container>
    </FillFlowContainer.ItemTemplate>
  </FillFlowContainer>
</CompositeDrawable>
```

| 기능 | 표기 | 생성되는 코드 |
| --- | --- | --- |
| 요소 | osu!framework 타입 이름 (`FillFlowContainer`, `BasicButton`) 또는 `local:MyDrawable` | `new T()` 후 `Add` / 루트는 `AddInternal` |
| 리터럴 | `Anchor="Centre"`, `RelativeSizeAxes="X\|Y"`, `Size="28"`, `Spacing="0,14"`, `Margin="l,t,r,b"`, `Colour="#1e1e2e"` 또는 `White`, `Font="Bold,44"` | enum, `Vector2`, `MarginPadding`, `Colour4.FromHex`, `FrameworkFont.Regular.With(...)` |
| 텍스트 내용 | `<SpriteText>Hello</SpriteText>` | `Text = "Hello"` |
| 단방향 바인딩 | `Text="{Bind Title}"` | 바운드 카피 + `BindValueChanged` |
| 양방향 바인딩 | `Current="{Bind NewText}"` (대상이 `Bindable<T>`) | `target.Current.BindTo(source)` |
| 변환 | `Converter=Method` (x:Class) 또는 `Converter=local:Type.Method`, `StringFormat='{0} items left'` | 메서드 호출 / `string.Format` |
| 애니메이션 바인딩 | `Animate=250, Easing=OutQuint` | 첫 값은 즉시, 이후 변경은 `TransformTo` |
| 이벤트 / 액션 | `Action="{Bind AddItem}"`, `OnCommit="{Bind AddItem}"`, `Action="OnClick"` (코드비하인드) | 람다 |
| 리스트 | `ItemsSource="{Bind Items}"` + `<X.ItemTemplate>` | `BindableList` 변경을 행 단위로 추가/삭제 |
| 템플릿에서 루트 접근 | `{Bind RemoveItem, Source=Root}` | 한 개짜리 인자에는 현재 아이템을 넘김 |
| 이름 | `x:Name="input"` | `private BasicTextBox input;` |
| 다른 요소에 바인딩 | `Text="{Bind Value, ElementName=volume}"` | 모든 요소가 만들어진 뒤 연결 |
| 정적 값 / null | `Foreground="{x:Static local:Palette.Pink}"`, `Content="{x:Null}"` | 필드·프로퍼티 참조 / `null` |
| WPF 표기 | `{Binding Title}`은 `{Bind Title}`과 같습니다 | |

## 템플릿, 리소스, 스타일

전부 빌드 시점에 풀립니다. 리소스 키나 스타일 대상이 틀리면 XAML 줄 번호가 붙은 빌드 오류(OX0012 등)가 나고, 런타임에는 사전 조회가 없습니다.

```xml
<Window x:Class="Demo.MainWindow" x:DataType="local:PeopleViewModel" Title="People">
  <Window.Resources>
    <Colour4 x:Key="Warm">#fab387</Colour4>
    <Style TargetType="Button">                       <!-- 키 없는 스타일: 범위 안의 모든 Button -->
      <Setter Property="CornerRadius" Value="12" />
    </Style>
    <Style x:Key="Danger" TargetType="Button">
      <Setter Property="Background" Value="{StaticResource Warm}" />
    </Style>
    <DataTemplate x:Key="PersonCard" x:DataType="local:Person">
      <StackPanel Orientation="Horizontal" Spacing="8">
        <TextBlock Text="{Bind Name}" />
        <TextBlock Text="{Bind Age, StringFormat='({0})'}" Foreground="{StaticResource Warm}" />
      </StackPanel>
    </DataTemplate>
  </Window.Resources>

  <ListBox ItemsSource="{Bind People}" SelectedItem="{Bind Selected}" ItemTemplate="{StaticResource PersonCard}" />
</Window>
```

| 기능 | 표기 | 동작 |
| --- | --- | --- |
| 데이터 템플릿 | `<ListBox.ItemTemplate><DataTemplate>...` | 아이템 타입은 `ItemsSource`에서 추론합니다 (`BindableList<Person>`이면 `Person`). `x:DataType`으로 지정할 수도 있습니다 |
| 텍스트 템플릿 | `DisplayMemberPath="Name"`, `DisplayMemberBinding="{Bind Name}"` | 그 멤버를 보여주는 `TextBlock` 템플릿으로 컴파일 |
| 콘텐츠 템플릿 | `Content="{Bind Selected}"` + `<ContentControl.ContentTemplate>` | `XTemplate`의 아이템 타입은 같은 요소의 `X` 바인딩 타입에서 추론 |
| 계층 템플릿 | `<HierarchicalDataTemplate ItemsSource="{Bind Children}">` | 각 아이템의 자식 목록을 바운드 카피로 넘김 (TreeView용) |
| 컨트롤 템플릿 | `<Button.Template><ControlTemplate TargetType="Button">` + `{TemplateBinding Background}` + `<ContentPresenter />` | 컨트롤의 기본 모양을 통째로 교체 |
| 아이템 바인딩 | `Binding="{Bind Done}"` (DataGrid 열 등) | `ItemBinding<Todo, bool>` 접근자로 컴파일. 리플렉션 없음 |
| 리소스 | `<X.Resources>`의 `x:Key` 항목, `{StaticResource Key}` | 텍스트 리소스는 대상 타입의 리터럴로, 템플릿은 그 자리에서 컴파일, 다른 요소는 쓸 때마다 새로 생성 |
| 공용 리소스 | 루트가 `<ResourceDictionary>`인 `.xaml` 파일 | 모든 페이지에서 보입니다 (WPF의 App.xaml 리소스 역할) |
| 스타일 | `<Style TargetType="Button">`, `Style="{StaticResource Key}"`, `BasedOn`, `EventSetter` | 세터를 요소 생성 코드에 인라인. 요소에 직접 쓴 값이 이깁니다 |
| 암시적 템플릿 | `<DataTemplate DataType="{x:Type local:Person}">` (키 없음) | 같은 아이템 타입의 ItemsControl에 자동 적용 |
| 컬렉션 | `<local:Bag.Tags><x:String>a</x:String></local:Bag.Tags>`, `<ItemsPanelTemplate>` | 기존 컬렉션에 `Add`, 래퍼는 생략 가능 |
| 기본 변환기 | `Converter=Not`, `BoolToVisibility`, `InverseBoolToVisibility`, `IsNull`, `IsNotNull`, `NullToVisibility` 등 | x:Class에 같은 이름이 없으면 `OsuXaml.Converters`에서 찾음 |
| 전역 속성 | `Visibility="Collapsed"`, `HorizontalAlignment`, `ToolTip`, `ContextMenu` | `[assembly: XamlGlobalProperties(typeof(T))]`로 등록한 정적 `SetX(Drawable, 값)` |

선택 상태(`SelectedItem`, `IsChecked`, `IsExpanded`, `IsOpen` 등)는 `Bindable<T>` 프로퍼티라서 뷰모델의 `Bindable<T>`와 양방향으로 연결됩니다. `SelectedItem`은 `Bindable<object>`지만 뷰모델의 `Bindable<Person>`과도 바로 묶입니다.

## WPF식 컨트롤

![gallery](docs/gallery-demo.gif)

`xmlns="osu-xaml"` 기본 네임스페이스에서 `OsuXaml.Controls`를 osu!framework보다 먼저 찾습니다. 그래서 `Button`, `TextBox`는 WPF식 컨트롤이 되고, osu!framework 컨트롤은 `BasicButton`처럼 원래 이름으로 계속 쓸 수 있습니다.

```xml
<DockPanel RelativeSizeAxes="Both">
  <Border DockPanel.Dock="Top" Background="#181825" Padding="20,14">
    <TextBlock Text="{Bind Greeting}" FontSize="26" FontWeight="Bold" />
  </Border>
  <Grid Padding="24" ColumnDefinitions="110,*,Auto" RowDefinitions="Auto,Auto,*" ColumnSpacing="12" RowSpacing="16">
    <TextBlock Text="Volume" VerticalAlignment="Center" />
    <Slider Grid.Column="1" Value="{Bind Volume}" />
    <TextBlock Grid.Column="2" Text="{Bind Volume, StringFormat='{0:0}%'}" />
    <StackPanel Grid.Row="1" Grid.ColumnSpan="3" Orientation="Horizontal" Spacing="10" HorizontalAlignment="Right">
      <Button Content="Cancel" />
      <Button Content="Save" Click="{Bind Save}" />
    </StackPanel>
  </Grid>
</DockPanel>
```

| 분류 | 컨트롤 | 주요 속성 |
| --- | --- | --- |
| 패널 | `Grid` | `RowDefinitions`/`ColumnDefinitions` (`Auto`, `120`, `*`, `2*`), `RowSpacing`, `ColumnSpacing`, 첨부 `Grid.Row/Column/RowSpan/ColumnSpan` |
| | `StackPanel` | `Orientation`, `Spacing` |
| | `DockPanel` | `LastChildFill`, 첨부 `DockPanel.Dock` |
| | `WrapPanel` | `Orientation`, `Spacing` |
| | `Canvas` | 첨부 `Canvas.Left/Top/Right/Bottom` |
| | `UniformGrid` | `Rows`, `Columns` |
| 콘텐츠 | `Border` | `Background`, `BorderBrush`, `BorderThickness`, `CornerRadius`, `Padding` |
| | `ScrollViewer` | 자식 하나를 세로 스크롤 |
| 컨트롤 | `TextBlock` | `Text`, `FontSize`, `FontWeight`, `Foreground` |
| | `Button` | `Content` 또는 자식 요소, `Click` 이벤트, `Action`, `IsEnabled`, 호버/누름 애니메이션 |
| | `CheckBox` | `IsChecked` (양방향), `Content` |
| | `TextBox` | `Text` (양방향) |
| | `Slider` | `Value` (양방향), `Minimum`, `Maximum` |
| | `ProgressBar` | `Value`, `Minimum`, `Maximum`, 값 변경 시 애니메이션 |
| | `Separator` | 가로 구분선 |
| 공통 | 모든 요소 | `HorizontalAlignment`, `VerticalAlignment` (`Stretch` 기본), `Margin`, `Width`/`Height` |

패널과 `ItemsSource`도 함께 쓸 수 있습니다 (`<WrapPanel ItemsSource="{Bind Tags}">`). `{Bind}`처럼 경로를 비우면 아이템 자체에 바인딩됩니다.

### 레이아웃 동작

- 매 프레임 자식 업데이트가 끝난 뒤(`UpdateAfterChildren`) 패널이 자식을 측정(Measure)하고 배치(Arrange)합니다. 이 시점에는 자동 크기 자식의 크기가 이미 확정되어 있습니다.
- 자식마다 `Slot`이 하나씩 붙어서 정렬과 여백을 처리합니다. 기본값은 WPF처럼 `Stretch`이고, 크기를 명시한 자식은 가운데에, 텍스트처럼 내용 크기인 자식은 시작 위치에 놓입니다.
- 다른 패널 안에 있지 않고 크기도 없는 패널은 내용 크기에 맞춰 스스로 크기를 정합니다.
- 첨부 속성은 생성기가 `Grid.SetRow(element, 1)` 같은 정적 메서드 호출로 바꿉니다. `Set이름(Drawable, 값)` 형태의 정적 메서드만 있으면 직접 만든 첨부 속성도 똑같이 동작합니다.
- `RowDefinitions` 문법 오류는 빌드 오류로 나옵니다. 정적 `Parse(string)` 메서드가 있는 타입은 모두 문자열로 쓸 수 있습니다.

## 수명 관리

뷰모델은 바운드 카피를 약한 참조로만 들고 있으므로, 생성된 코드는 카피를 `BindingScope` 필드에 모읍니다. osu!framework는 Drawable을 해제할 때 `IUnbindable` 필드를 모두 해제하므로, 뷰가 사라지면 바인딩도 자동으로 끊깁니다. 리스트의 각 행은 자기 스코프를 가지며, 행이 삭제될 때 함께 끊깁니다. (이 구조는 헤드리스 테스트가 뷰 교체 시 해제된 컨테이너를 건드리는 버그를 잡아내서 도입했습니다.)

## 아직 없는 것

- 핫 리로드 연결 (`HotReloadCallbackReceiver`로 뷰 재생성), IDE 자동완성 스키마
- 스타일/리소스 딕셔너리, `<Box.Colour>` 같은 속성 요소, 제네릭 타입 요소
- ComboBox, ListBox, TabControl, 스타일/템플릿 시스템. 여러 칸에 걸친 자식은 Auto 크기 계산에 들어가지 않습니다 (WPF는 반영합니다)
- 패널은 매 프레임 측정합니다. 프레임당 캐시는 있지만 무효화 기반 최적화는 아직 없습니다
- 백그라운드 스레드에서 뷰모델을 바꿀 때의 Update 스레드 마샬링
- 한글 글꼴: osu!framework 기본 Roboto에는 한글이 없어 샘플은 영어 UI입니다
