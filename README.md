# OsuXaml: osu!framework 위의 컴파일형 XAML

osu!framework 타입 이름을 그대로 쓰는 XAML을 빌드 시점에 C#으로 바꾸는 소스 생성기와, 그것으로 만든 할 일(Todo) 앱입니다. 구현 가능성 검증용 프로토타입입니다.

![demo](docs/todo-demo.gif)

## 구성

| 경로 | 내용 |
| --- | --- |
| `src/OsuXaml.Generator` | Roslyn Incremental Source Generator. `*.xaml`을 읽어 `InitializeComponent()`를 생성 |
| `src/OsuXaml` | 생성된 코드가 호출하는 런타임 (`XamlBinding`, `BindingScope`). 리플렉션 없음 |
| `samples/TodoApp` | XAML 한 파일 + 뷰모델로 만든 할 일 앱 |
| `tests/OsuXaml.Tests` | 생성기 테스트 7개 + 헤드리스 osu!framework 앱 테스트 3개 |

## 실행

```bash
dotnet run --project samples/TodoApp     # 앱 실행 (.NET 10 SDK)
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

## 수명 관리

뷰모델은 바운드 카피를 약한 참조로만 들고 있으므로, 생성된 코드는 카피를 `BindingScope` 필드에 모읍니다. osu!framework는 Drawable을 해제할 때 `IUnbindable` 필드를 모두 해제하므로, 뷰가 사라지면 바인딩도 자동으로 끊깁니다. 리스트의 각 행은 자기 스코프를 가지며, 행이 삭제될 때 함께 끊깁니다. (이 구조는 헤드리스 테스트가 뷰 교체 시 해제된 컨테이너를 건드리는 버그를 잡아내서 도입했습니다.)

## 아직 없는 것

- 핫 리로드 연결 (`HotReloadCallbackReceiver`로 뷰 재생성), IDE 자동완성 스키마
- 스타일/리소스 딕셔너리, `<Box.Colour>` 같은 속성 요소, 제네릭 타입 요소
- WPF식 Grid(Star/Auto) 같은 레이아웃 패널
- 백그라운드 스레드에서 뷰모델을 바꿀 때의 Update 스레드 마샬링
- 한글 글꼴: osu!framework 기본 Roboto에는 한글이 없어 샘플은 영어 UI입니다
