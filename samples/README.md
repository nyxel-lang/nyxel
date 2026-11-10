# samples/

用已定的语法（ADR-0005 到 ADR-0035）写的示例，用来整体检查语言读起来怎么样。现在还没有编译器，这些文件只是设计材料；有了 Parser 之后，它们是第一批必须能解析的输入。

| 文件 | 展示 |
|---|---|
| Combat/Damage.nyxel | 带数据的 `enum`（数据同元组，PascalCase）、enum 里的计算属性和方法、带数据的 enum 实现接口、`match` 按位置取 case 数据（`_` 丢弃）、接口 |
| Combat/Health.nyxel | struct、命名 `init`、计算属性（`get = 表达式`）、`var self` 方法、读 enum 的属性、`as float` 转换、文档注释的 `- Parameter` / `- Returns` 和链接 |
| Combat/Fireball.nyxel | `init super(...)` 调用基类 init、`init self(...)` 委托、`super.Update`、括号里换行、整数乘小数字面量 |
| Common/Collections.nyxel | 泛型：泛型扩展 `extension<T> List<T>`（带约束的、方法自己的类型参数 `Map<U>`）、`T: class` 时的 `T?` 和直接创建的 `array<T?>`、`notnull` 约束、`for i in 1..<n`、文档注释写泛型参数 `- Parameter U:` 和 `- Throws` |
| Common/TeamMapping.nyxel | 命名空间别名 `import Game.Protocol under Wire` 和 `Wire.Team`、自己的同名 `Team` 优先于导入的、`match` 一个 C# enum（`case _` 兜底） |
| Common/Fixed.nyxel | 定点数：`operator` 定义运算符（一元 `-`、同一个运算符按操作数类型写两个）、只写 `<`（`>=` 由它生成）、`operator as` 转换和 `x as Fixed` / `x as float`、struct 的 `==` 比内容、`+=` 用 `+`、私有命名 init、静态字段 |
| Common/VectorExtensions.nyxel | `extension` 块：实例扩展、静态扩展、行尾 `=` 续行 |
| Common/HighScore.nyxel | 当值用的 `try`（`try 值 catch e: T -> 值`，`catch` 分支里 `return`）、`eval` 块（提前 `give`，临时变量只在块里）、`give` 后面接当值用的 `if`（`if c then a else b`）、`out let`、`try` 语句里的 `using`、`new ... with (...)` 创建时设属性 |
| Common/Scoreboard.nyxel | 元组：返回元组、`(name, score)` 按元素类型取名、按位置解构、`for (key, value) in` 遍历字典、`for (i, entry) in ….Index()` 同时拿下标、泛型参数里的元组类型 |
| Common/TileMap.nyxel | `enum Tile : byte`、索引器 `property self[x: int, y: int]` 和 `self[x, y]`、二维数组 `array2d<Tile>` 和 `tiles[x, y]`、切片 `lines[1...]`、`(0..<n).StepBy(2)`、`(0..<n).Reversed()` 边遍历边删、集合字面量（字段的 `[]`、带元组元素的静态数组、`for tile in [...]`）、当值用的 `if` 当实参、文档注释的 `- Parameter` / `- Returns` / `- Throws` 和链接 |
| Common/FollowCamera.nyxel | `protected override`、`ref` 实参、字段 `?? return`、`??=` |
| Ecs/Spin.nyxel | 照 EnginePlayground 的旋转系统写的 ECS 脚本：`Span<Transform>` 的元素上调用 `var self` 扩展（直接改引擎内存）、`ReadOnlySpan`、`world.Get<T>(e)` 的 `ref` 返回值、在临时值上连着调用查询构建器、组件 struct 的字段初始值 |
| Enemies/BossIntro.nyxel | 过场流程：`await all` 同时做两件事、当值用按位置解构结果，`await any` 按键或超时、“一边蓄力一边抖屏”、`case let` 起名、当值用的 `-> 值` 和 `-> eval { give }`，`await using` 释放文件，`await for` 逐行读文件，`?? throw` |
| Enemies/Slime.nyxel | 继承引擎类（没写 `init`，自动有无参 init）、`launch` 受击闪烁协程、`event` / `emit`、带数据的 enum 状态机、`is` 判断 case、调用扩展方法、`?? return`、命名实参、插值字符串、静态字段 |
| Enemies/Spawner.nyxel | `async` 波次流程（`Wait.Until`、`Wait.Seconds`）、`launch` 绑定对象、`+=` 订阅事件、自动属性 `private set`、`for` + `..<`、静态扩展、lambda、多行方法链、泛型调用、`is not` 收窄、LINQ |
| Enemies/Turret.nyxel | `flags enum` 和 `HasFlag`、写明每个值的 `enum TurretModel : byte` 和它的方法、`struct enum` 状态（不分配，带数据的 enum 必须写明 `struct` 或 `class`，ADR-0039）、`is not` 判断 case、`match` 按位置取数据并自己起名、`when`、`return match` |

`Engine` 命名空间是假想的引擎 API（`Behaviour`、`World`、`Log`、`Collider`、`Projectile`、`Smooth`、`Color`、`Input`、`Key`），Ecs/Spin.nyxel 里的 `World`、`Transform`、`Entity`、`Query` 照 EnginePlayground 的 Engine.Managed（`world.Get<T>(e)` 返回 `ref T`，`chunk.Get<T>()` 返回 `Span<T>`），`Game.Protocol` 是假想的网络协议生成代码（有一个 `Team` enum），`Player` 是假想的另一个脚本，都只为展示语法。`Projectile` 继承 `Behaviour`，有 `init(power: int)` 和 `Power` 属性；`Behaviour` 有 `Tint` 属性和 `Camera`、`Hud`（返回协程的 `PanTo`、`ShakeForever`、`ShowTitle` 等），`OnEnable` 是 `protected virtual`，并实现了 Nyxel 的生命周期接口（销毁后协程自动停止）；`Smooth.Damp` 类似 Unity 的 `SmoothDamp`，速度用 `ref` 参数传。`System`、`System.IO`、`System.Numerics`、`System.Linq` 是真实的 .NET API；`Nyxel`（`Wait`、`Coroutine`）是 Nyxel.Runtime 将要提供的 API。

language-reference 各节末尾“待定”的细节示例里刻意没用到。
