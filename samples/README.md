# samples/

用已定的语法（ADR-0005 到 ADR-0024）写的示例，用来整体检查语言读起来怎么样。现在还没有编译器，这些文件只是设计材料；有了 Parser 之后，它们是第一批必须能解析的输入。

| 文件 | 展示 |
|---|---|
| Combat/Damage.nyxel | 带数据的 `enum`（数据同元组，PascalCase）、接口 |
| Combat/Health.nyxel | struct、命名 `init`、计算属性、`var self` 方法、`match` 按位置取 case 数据（`_` 丢弃）、`as float` 转换 |
| Combat/Fireball.nyxel | `super.init` 调用基类 init、`self.init` 委托、`super.Update`、括号里换行、整数乘小数字面量 |
| Common/VectorExtensions.nyxel | `extension` 块：实例扩展、静态扩展、行尾 `=` 续行 |
| Common/HighScore.nyxel | 当表达式用的 `try`（`catch` 分支里 `return`）、`out let`、`try` 里的 `using` |
| Common/Scoreboard.nyxel | 元组：返回元组、`(name, score)` 按元素类型取名、按位置解构、`for (key, value) in` 遍历字典、`for (i, entry) in ….Index()` 同时拿下标、泛型参数里的元组类型 |
| Common/TileMap.nyxel | `enum Tile : byte`、二维数组 `array2d<Tile>` 和 `tiles[x, y]`、切片 `lines[1...]`、`(0..<n).StepBy(2)`、`(0..<n).Reversed()` 边遍历边删、集合字面量（字段的 `[]`、带元组元素的静态数组、`for tile in [...]`）、`if` 表达式当实参 |
| Common/FollowCamera.nyxel | `protected override`、`ref` 实参、字段 `?? return`、`??=` |
| Enemies/Slime.nyxel | 继承引擎类（没写 `init`，自动有无参 init）、`launch` 受击闪烁协程、`event` / `emit`、带数据的 enum 状态机、`is` 判断 case、调用扩展方法、`?? return`、命名实参、插值字符串、静态字段 |
| Enemies/Spawner.nyxel | `async` 波次流程（`Wait.Until`、`Wait.Seconds`）、`launch` 绑定对象、`+=` 订阅事件、自动属性 `private set`、`for` + `..<`、静态扩展、lambda、多行方法链、泛型调用、`is not` 收窄、LINQ |
| Enemies/Turret.nyxel | `flags enum` 和 `HasFlag`、写明每个值的 `enum TurretModel : byte`、`struct enum` 状态（不分配）、`is not` 判断 case、`match` 按位置取数据并自己起名、`when`、`return match` |

`Engine` 命名空间是假想的引擎 API（`Behaviour`、`World`、`Log`、`Collider`、`Projectile`、`Smooth`、`Color`），`Player` 是假想的另一个脚本，都只为展示语法。`Projectile` 继承 `Behaviour`，有 `init(power: int)` 和 `Power` 属性；`Behaviour` 有 `Tint` 属性，`OnEnable` 是 `protected virtual`，并实现了 Nyxel 的生命周期接口（销毁后协程自动停止）；`Smooth.Damp` 类似 Unity 的 `SmoothDamp`，速度用 `ref` 参数传。`System`、`System.IO`、`System.Numerics`、`System.Linq` 是真实的 .NET API；`Nyxel`（`Wait`）是 Nyxel.Runtime 将要提供的 API。

language-reference 各节末尾“待定”的细节（索引器、对象初始化器等）示例里刻意没用到。
