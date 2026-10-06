# Nyxel 语言参考（草稿）

描述语言**是什么**，随设计讨论逐节补全；**为什么**这样定见各节标注的 ADR。还没定的部分标“待定”，不要按猜测实现。

## 总体风格（ADR-0005）

- 代码块用花括号，结构由括号决定，与缩进无关。
- 声明以关键字开头，类型写在名字后面：`hp: int`。
- 语句以换行结束，不写分号。哪些换行不结束语句见“换行与续行”。
- `if` / `while` 的条件不加括号，后面的块必须有花括号。
- **冒号后面是类型，等号后面是值**（ADR-0011）：`var hp: int = 100`、`count: int = 1`、`class Slime : Behaviour`、`Spawn(count = 3)`。例外只有插值字符串里的格式说明符 `{speed:F2}`。

## 换行与续行（ADR-0013）

```nyxel
// 圆括号、方括号里随便换行
let slime = self.World.Spawn<Slime>(
    position = self.Position + offset,
    rotation = Quaternion.Identity)

// 下一行以 . 或 ?. 开头：接着上一行
let targets = self.enemies
    .Where(func(e) = not e.IsDead)
    .OrderBy(func(e) = e.Hp)

// 下一行以二元运算符开头：接着上一行
let canAttack = self.cooldown <= 0
    and target != null
    and not target.IsDead

// 行尾是 = 或 ->：接着下一行
let nearest =
    self.NearestTo(point = self.Position) ?? return

if self.IsDead {
    return
} else {                // else 跟在 } 后面同一行
    self.Move()
}
```

- 圆括号、方括号里的换行不结束语句。
- 下一行以 `.`、`?.` 或二元运算符（`+`、`and`、`or`、`??`、`is`、`==` 等）开头时接着上一行。
- 一行以 `=` 或 `->` 结尾时接着下一行。
- 其余换行都结束语句。
- 运算符一律放在行首，括号里也一样（ADR-0019）：二元运算符和 `.`、`?.` 写在行尾都是编译错误，诊断提示挪到下一行开头。
  ```nyxel
  let d = Vector3.Distance(
      self.Position
          + offset,            // 正确
      target)
  let e = Vector3.Distance(
      self.Position +          // 错误
          offset,
      target)
  ```
- `{` 必须和前面的内容在同一行；`else` 必须跟在 `}` 后面同一行。
- 每条语句、每个成员、每个 `case`、每个访问器各占一行；`{` 后面的第一个和 `}` 前面的最后一个可以和括号写在同一行（`get { return self.hp }`、`{ get }`）。
- 待定：`+=` 等复合赋值写在行尾时是否接着下一行（目前不行，只有 `=` 和 `->`）。

## 文件结构（ADR-0012）

```nyxel
namespace Game.Enemies

import System
import System.Collections.Generic
import Engine

public class Spawner : Behaviour {
    ...
}
```

- 顺序固定：`namespace`（最多一个，作用于整个文件；不写时在全局命名空间）、`import`、声明。
- `import 命名空间` 导入整个命名空间。每个文件显式列出，没有全局导入和隐式导入。内置类型名不用导入；外层命名空间的类型按 C# 规则可见。
- 待定：导入别名、静态导入。

## 关键字风格（ADR-0006）

关键字用完整英文单词；只有在主流语言里通用、能读出来的缩写可以例外（`var`、`func`、`enum`、`struct`、`init`）。不用 `fn`、`pub`、`mut`、`impl` 这类缩写。拿不准的细节优先参照 Swift。

## 注释（ADR-0007）

```nyxel
/// 在距离最近的出生点生成敌人。
///
/// 文档注释的内容是 Markdown，编译器转成 .NET 的 XML 文档文件。
public func SpawnNearest(self, kind: EnemyKind) -> Entity {
    // 普通行注释
    ...
}
```

- `//` 行注释，`///` 文档注释（Markdown，写在声明前）。没有 `/* */` 块注释。
- 待定：文档注释里参数和返回值的写法约定（倾向 Swift 的 `- Parameter name:` / `- Returns:`）。

## 内置类型与字面量（ADR-0007）

内置类型名沿用 C# 的别名，含义相同：`bool` `byte` `sbyte` `short` `ushort` `int` `uint` `long` `ulong` `float` `double` `decimal` `char` `string` `object`。

数值字面量的类型由上下文（这个位置期望的类型）决定，没有类型后缀：

```nyxel
var speed: float = 2.5                  // float：声明的类型
let half = width * 0.5                  // width 是 float → 0.5 是 float
self.Position = new Vector3(0, 1.5, 0)  // 参数是 float → 1.5 是 float
let t: float = 3                        // 整数字面量可以用在浮点位置
let ratio = 2.5                         // 没有上下文 → double（同 C#）
let count = 10                          // 没有上下文 → int（同 C#）
```

- 没有上下文时：整数是 `int`（放不下依次用 `uint`、`long`、`ulong`），小数是 `double`。
- 混合运算（ADR-0013）：期望类型往里传，但不改变整数运算。

  ```nyxel
  // Wave: int，Speed: float
  slime.Speed = 2 + self.Wave * 0.5   // float
  slime.Speed = self.Wave / 2         // 整数除法再转 float（Wave 是 3 时得 1.0）
  let a = self.Wave * 0.5             // double（没有期望类型）
  let b: int = self.Wave * 1.5        // 错误：期望整数，1.5 不是整数
  let x: float = 1 / 2                // 0.5（两边都是字面量，看期望类型）
  ```

  1. 运算的一边是有确定类型的值（变量、字段、属性、调用结果）时，字面量跟着它。`self.Wave / 2` 不管放在哪里都是整数除法。
  2. 整数值配小数字面量时按浮点算：期望的类型是浮点类型就用它，没有期望类型时用 `double`。
  3. 两边都是字面量时看期望的类型。
  4. 期望的类型来自赋值目标、声明的类型、参数类型、返回类型、比较运算的另一边，经过算术运算符向里传。
- 显式转换写 `x as T`，见“类型转换”。
- 小数字面量不能用在整数位置；超出目标类型范围是编译错误。
- `2.5f`、`10L` 之类的后缀是编译错误。
- `0xFF`、`0b1010`、`1_000_000`、`1e-3` 同 C#。

## 字符串（ADR-0007）

```nyxel
Log.Info($"HP: {self.hp}/{self.maxHp}")
Log.Info($"Speed: {speed:F2} m/s")
let plain = "{ not interpolated }"
```

- 插值字符串 `$"..."` 与 C# 完全相同：格式说明符、对齐、`{{` / `}}`。
- 普通字符串的转义序列与 C# 相同。
- 待定：逐字字符串、原始字符串、多行字符串。

## 变量与字段（ADR-0006）

```nyxel
class Slime : Behaviour {
    let maxHp: int = 100        // 不可变字段：只能在声明处或 init 里赋值
    var hp: int = 100           // 可变字段

    func Heal(self, amount: int) {
        let healed = amount * 2         // 局部变量，类型推断为 int
        self.hp = Math.Min(self.hp + healed, self.maxHp)
    }
}
```

- `let` 不可变，`var` 可变。不可变的是名字本身，不是它指向的对象：`let enemies = new List<Enemy>()` 之后可以 `enemies.Add(e)`，但不能 `enemies = ...`。
- class 变量存的是引用（对象在哪），struct 变量存的是数据本身（赋值时复制一份）。所以 `let` 的 class 变量仍然可以改对象的字段，`let` 的 struct 整个不可变：不能给它的字段赋值，也不能调用它的 `var self` 方法（ADR-0008）。
- 给 `let` 重新赋值是编译错误。
- 字段必须写类型。局部变量有初始值时可以省略类型。
- 静态字段写 `static var` / `static let`（ADR-0008）。

## 函数与方法（ADR-0006、ADR-0008）

```nyxel
class Slime : Behaviour {
    static var AliveCount: int = 0
    var hp: int = 30

    public func TakeDamage(self, amount: int) {     // 第一个参数是 self：实例方法
        self.hp -= amount
        if self.hp <= 0 {
            self.Die()
        }
    }

    func Die(self) {
        Slime.AliveCount -= 1
        self.Destroy()
    }

    public func Distance(a: Vector3, b: Vector3) -> float {   // 没有 self：静态函数
        return (a - b).Length()
    }
}

struct Health {
    var current: int

    public func IsDead(self) -> bool {          // struct 的 self 方法不能修改自己
        return self.current <= 0
    }

    public func Damage(var self, amount: int) { // var self：可以修改自己
        self.current -= amount
    }
}

slime.TakeDamage(5)                 // 调用时不传 self
let d = Slime.Distance(a, b)        // 静态函数通过类型名调用
```

- `func Name(参数) -> 返回类型 { ... }`；没有返回值时省略 `->` 和返回类型。参数和返回类型必须写。
- 第一个参数是 `self`（不写类型）的是实例方法，没有 `self` 的是静态函数。没有 `static func`。
- `var self` 只能用在 struct 上，表示方法会修改这个值；只写 `self` 的 struct 方法不能修改字段，也不能调用 `var self` 方法。class 方法的 `self` 是引用，总能修改对象的字段，不需要也不允许写 `var self`。
- `var self` 方法不能在 `let` 的 struct 上调用，也不能在临时副本上调用（属性、索引器、函数调用的结果）：
  ```nyxel
  let h = new Health(max = 30)
  h.Damage(amount = 5)                    // 错误：h 是 let
  self.healths[0].Damage(amount = 5)      // 错误：改的是副本，修改会丢失
  var first = self.healths[0]             // 先取出，改完再写回
  first.Damage(amount = 5)
  self.healths[0] = first
  ```
- 待定：C# 里没有标 `readonly` 的 struct 方法在 `let` 和临时副本上怎么处理。
- 参数不能重新赋值，和 `let` 一样（ADR-0014），需要改就复制到局部变量：`let actual = amount - self.armor`。lambda 的参数同样。没有 `var` 参数。
- 实例方法通过实例调用（`slime.TakeDamage(5)`），静态函数通过类型名调用（`Slime.Distance(a, b)`），不能互换。
- 返回值必须写 `return`（ADR-0007）。
- 命名实参写 `名字 = 值`，写不写由调用者决定，混用规则同 C#（ADR-0011）：`self.Spawn(kind = EnemyKind.Slime, count = 3)`。
- 默认参数 `count: int = 1`，规则同 C#。
- 自己声明的函数不能重载（同一类型里不能同名）；`override` / 实现 .NET 里本来就重载的方法除外。
- 函数体可以是块，也可以是 `= 表达式`（ADR-0012）：`public func IsDead(self) -> bool = self.hp <= 0`。赋值不能作为表达式体。

### out / ref 参数（ADR-0015）

```nyxel
if self.cache.TryGetValue(id, out let enemy) {
    enemy.Alert()                       // 这里 enemy 一定有值
}

if not int.TryParse(text, out let count) {
    return
}
self.Spawn(count = count)               // if 后面也能用 count

self.Position = Smooth.Damp(self.Position, target, ref self.velocity, 0.3)
```

- 调用带 `out` / `ref` 参数的方法时，调用处必须写标记：
  - `out let x` / `out var x` 就地声明新变量，类型取参数的类型（有歧义时写 `out let n: int`）。
  - `out 目标` 写进已有的 `var` 局部变量或 `var` 字段；`out _` 丢弃。
  - `ref 目标`：目标必须是 `var` 局部变量、`var` 字段或数组元素，不能是属性或 `let`。
  - 命名实参写 `velocity = ref self.velocity`。C# 的 `in` 参数照常传值，不写标记。
- `if` 条件里用 `out let` 声明的变量，在 `if` 之后仍然可用；其他位置声明的，作用域到所在语句结束。
- 按 .NET 的可空标注收窄：`TryGetValue` 返回 `true` 的分支里，`out` 出来的值非空。
- 自己的函数只在 `override` 或实现接口、.NET 签名要求时才能声明：`data: out SaveData`、`velocity: ref Vector3`、`value: in Matrix4x4`。`out` 参数每条返回路径都要赋值；`ref` / `out` 参数可以赋值（普通参数不行）。自己的 API 表达“可能没有”用 `T?`。

## 扩展（ADR-0014）

```nyxel
namespace Game.Util

import System.Numerics

extension Vector3 {
    /// 去掉高度分量。
    public func Flat(self) -> Vector3 = new Vector3(self.X, 0, self.Z)

    public property IsZero: bool {
        get { return self == Vector3.Zero }
    }

    public func FromAngle(radians: float) -> Vector3 {     // 没有 self：静态扩展
        return new Vector3(MathF.Cos(radians), 0, MathF.Sin(radians))
    }
}

// 另一个文件 import Game.Util 之后：
let ground = self.Position.Flat()
let dir = Vector3.FromAngle(radians = 0.5)
```

- `extension 类型 { }` 写在文件顶层，给不是自己定义的类型（引擎、.NET）加方法和属性。块里的写法和类里一样：有 `self` 是实例方法，没有是静态函数；`static property` 是静态扩展属性。不能有字段和 `init`。
- 可见性同类成员：默认私有（只在这个块里可见），`public` 才能在块外用。使用方 `import` 扩展所在的命名空间后可见。类型本身的成员优先于扩展。
- 扩展只能访问类型的公开成员。
- C# 那边：C# 14 的 `extension` 块，放在 `static partial class Vector3Extensions` 里。
- 待定：泛型扩展（`extension<T> List<T>`）；修改 struct 自己的扩展（`var self`）。

## 函数类型与 lambda（ADR-0012）

```nyxel
var onDeath: func(Enemy) = func(e) { self.score += 10 }    // 没有返回值：不写 ->
let filter: func(Enemy) -> bool = func(e) = e.IsBoss

let alive = enemies.Where(func(e) = e.Hp > 0)

self.button.OnClick(func(evt) {
    self.clicks += 1
})

let nearest = enemies.MinBy(func(e: Enemy) -> float {
    return (e.Position - self.Position).Length()           // 从这个 lambda 返回
})
```

- 函数类型 `func(A, B) -> R`，没有返回值写 `func(A, B)`。对应 .NET 的 `Func<>` / `Action<>`；写 `Func<>` / `Action<>` 给警告。
- lambda 是去掉名字的函数：`func(参数) { 块 }` 或 `func(参数) = 表达式`。参数类型、返回类型能推断时可省略。
- lambda 块里的 `return` 从 lambda 返回；`if` / `match` 块里的 `return` 从外层函数返回。
- 捕获规则同 C#；方法里的 lambda 可以用 `self`。
- 成员函数仍用 `func Name(...)` 声明。
- 在需要函数类型的地方，`对象.方法` / `类型名.函数` 不带括号就是这个函数本身（ADR-0017）：`slime.Died += self.OnSlimeDied`、`names.Select(Slime.Describe)`。别处不带括号写方法名是编译错误。

## 泛型（ADR-0012）

```nyxel
func Max<T: IComparable<T>>(a: T, b: T) -> T {
    return if a.CompareTo(b) >= 0 { a } else { b }
}

class Pool<T: class and IPoolable> {
    var free: Stack<T> = new Stack<T>()
}

let boss = World.Spawn<Slime>()
let biggest = Max(a = 3, b = 7)        // T 推断为 int
```

- `<T>` 同 C#；类型实参推断同 C#。
- 约束写在 `<>` 里，冒号后是类型；多个约束用 `and`。特殊约束 `class`、`struct`、`unmanaged`、`new()`。
- 待定：泛型的可空性；协变 / 逆变。

## 成员访问（ADR-0008）

- 实例成员一律写 `self.`：`self.hp`、`self.Die()`。
- 静态成员一律写类型名：`Slime.AliveCount`、`Slime.Distance(a, b)`。
- 在重写的方法里调用基类的实现写 `super.`：`super.Update(dt = dt)`（ADR-0013）。
- 不带接收者的名字只能是局部变量或参数。直接写字段名是编译错误。
- 私有字段不加前缀。

## 类型定义（ADR-0008）

### class、struct、interface

与 C# 相同：class 是引用类型，struct 是值类型。

```nyxel
interface IDamageable {
    func TakeDamage(self, amount: int)
}

public class Slime : Behaviour, IDamageable {
    ...
    public override func Update(self, dt: float) { ... }
}
```

- 继承和实现接口写在 `:` 后面。`virtual` / `override` / `abstract` / `sealed` 的用词和含义同 C#。
- 接口名以 `I` 开头。接口成员不写可见性，默认公开（同 C#）；实现它的成员要写 `public`。
- 类型默认只在本程序集可见，给其他程序集用写 `public`。

### enum

```nyxel
enum Element {
    case Fire
    case Ice
    case Lightning
}

enum Damage {
    case Physical(amount: int)
    case Burn(amount: int, seconds: float)
    case Heal(amount: int)
}

let element = Element.Fire                              // 不带数据的 case：常量
let hit = new Damage.Burn(amount = 12, seconds = 3)     // 带数据的 case：new
```

- 每种情况一行，以 `case` 开头。case 带的数据每一项都要有名字。
- 全部 case 不带数据时等同 C# 的枚举；有带数据的 case 时，每个值是堆上的对象。
- case 的数据用 `match` 按字段名取出（见“match”）。
- 待定：简单枚举的底层值与 flags；带数据枚举的值类型版本。

### 构造

```nyxel
struct Point {
    public let X: float
    public let Y: float

    public init(x: float, y: float) {                   // 不带名字的 init：最多一个
        self.X = x
        self.Y = y
    }

    public init FromPolar(radius: float, angle: float) { // 命名 init
        self.X = radius * MathF.Cos(angle)
        self.Y = radius * MathF.Sin(angle)
    }
}

let a = new Point(x = 1, y = 2)
let b = new Point.FromPolar(radius = 1, angle = 0.5)
```

- 不带名字的 `init` 最多一个，其余必须有名字。构造函数不按参数类型重载。
- 没有默认值的字段，每个 `init` 结束前都必须赋过值（确定初始化检查）。
- `init` 不写 `public` 就是私有的。`init` 里的 `self` 是正在构造的对象。
- C# 那边：不带名字的 `init` 是构造函数，命名 `init` 是同名静态方法（`Point.FromPolar(1, 0.5)`）。
- 没写 `init` 的类自动有一个公开的无参 `init`（ADR-0013），前提是每个字段都有初始值，否则编译错误。声明了任何 `init` 就不再自动生成；声明了 `init` 的 struct 不能写 `new Health()`。

### 基类与 super（ADR-0013）

```nyxel
public class Fireball : Projectile {
    public let Radius: float

    public init(power: int, radius: float) {
        super.init(power = power)           // 构造基类部分：必须是第一句
        self.Radius = radius
    }

    public init Small() {
        self.init(power = 5, radius = 0.5)  // 委托给本类的另一个 init
    }

    public override func Update(self, dt: float) {
        super.Update(dt = dt)               // 调用基类的同名方法
    }
}
```

- `super.init(...)` 只能是 `init` 的第一句，参数里不能用 `self`。没写时自动调用基类的无参 `init`，基类没有就是编译错误。
- 本类 `init` 之间委托写 `self.init(...)`，也只能是第一句。
- 重写的方法里调用基类方法写 `super.方法名(...)`。
- C# 那边：`super.init(...)` 是 `: base(...)`，`self.init(...)` 是 `: this(...)`，`super.Update` 是 `base.Update`。
- 待定：调用基类的命名 `init`。

### new

- 创建值一律写 `new`：`new Type(...)`、`new Type.Name(...)`（命名 init 或带数据的 case）、`new List<Enemy>()`。
- 不带 `new` 的 `名字(...)` 一定是函数调用。

## if 表达式（ADR-0007）

```nyxel
let label = if self.hp > 0 { "alive" } else { "dead" }

let speed = if self.IsFrozen {
    0
} else if self.IsHasted {
    self.BaseSpeed * 2
} else {
    self.BaseSpeed
}
```

- `if` 和 `match` 可以作为表达式：分支块里最后一个表达式是该分支的值；当表达式用时必须有 `else`。
- 没有 `?:` 三元运算符。
- 函数体必须用 `return` 返回值。

## match（ADR-0009）

```nyxel
let change = match hit {
    case Physical(amount) -> -amount
    case Burn(amount, seconds) -> -amount
    case Heal(amount) -> amount
}

match hit {
    case Burn(seconds) -> {         // 只取需要的字段，顺序无关
        self.StartBurning(seconds)
    }
    case Physical -> {}             // 不需要数据时不写括号
    else -> {}
}
```

- 每个分支一行，以 `case` 开头；默认分支 `else`。分支用 `->`（Nyxel 没有 `=>`）。
- 分支右边是一个表达式或一个块；当表达式用时，块的值是最后一个表达式。
- case 的数据按字段名取出，成为分支里的局部变量；可以只列一部分，顺序无关。不支持改名：与已有的局部变量或参数重名是编译错误。
- 必须覆盖所有情况（当语句用也一样）：enum 列全所有 case 或写 `else`；其他类型必须有 `else`。“其余不处理”写 `else -> {}`。
- 模式（ADR-0010）：
  - 常量 `case 0`、`case "boss"`、`case null`；
  - 比较 `case < 10`；区间 `case 10..<50`、`case 1...3`；
  - 组合 `case 1 or 2`、`case > 0 and < 10`、`case not null`；
  - 类型 `case is Enemy`（被匹配的局部变量 / 参数在分支里收窄成该类型）；
  - 附加条件 `case Burn(seconds) when seconds > 3`。

## 空值（ADR-0009）

```nyxel
class Turret : Behaviour {
    var target: Enemy? = null           // 可能为 null
    let owner: Player                   // 一定有值

    func Fire(self) {
        let target = self.FindTarget() ?? return    // 没有就返回
        target.TakeDamage(10)                       // 这里 target 是 Enemy

        let weapon = self.weapon                    // 字段先存进局部变量
        if weapon != null {
            weapon.Reload()                         // 检查过，当作非空
        }

        let name = self.target?.Name ?? "nobody"
        let player = World.FindPlayer()
            ?? throw new InvalidOperationException("场景里没有玩家")
    }
}
```

- `T` 一定有值，`T?` 可能为 `null`，违反是编译错误。值类型 `int?` 同 C#。
- C# 程序集的可空标注照单全收；没有标注的程序集，返回值和字段当作 `T?`，参数可以接受 `null`。
- 检查后自动收窄：只对局部变量和参数；字段、属性、调用结果先存进局部变量。
- `?.`、`??` 同 C#；`??` 右边可以是 `return`、`throw`、`continue`、`break`。
- 没有 `x!` / `x!!`；断言非空写 `?? throw new 异常("原因")`。
- 待定：数组元素的默认值；`== null` 的翻译（引擎重载的 `==`）；`??=`。

## 循环与区间（ADR-0010）

```nyxel
for enemy in self.enemies {
    enemy.Alert()
}

for i in 0..<self.waveCount {       // 0 到 waveCount - 1
    self.SpawnWave(i)
}

for level in 1...3 {                // 1、2、3
    self.Unlock(level)
}

while self.hp > 0 {
    ...
}
```

- `for 名字 in 集合`，循环变量不可变。`while 条件 { }`。没有 C 风格的 `for`，没有 `do` / `while`。
- `a..<b` 不含 `b`，`a...b` 含 `b`。没有单独的 `..`。
- 区间的优先级比算术低、比比较高（ADR-0019）：`0..<self.count - 1` 是 `0..<(count - 1)`。比较、`and` / `or`、`??` 写在两边要加括号。区间不能连写（`a..<b..<c` 是错误）。
- `break` / `continue` 同 C#。
- 待定：带标签的 `break`；区间作为值和切片；步长、倒序；同时拿下标。

## 运算符（ADR-0010）

```nyxel
if not self.IsDead and target != null {
    self.Attack(target)
}
let canJump = self.onGround or self.coyoteTime > 0
self.combo += 1
```

- 逻辑运算用单词 `and`、`or`、`not`（短路、优先级同 C#）。没有 `&&`、`||`、`!`。
- 没有 `++` / `--`，写 `+= 1`。
- 算术、比较、位运算、复合赋值、整数除法、隐式数值转换同 C#。
- 赋值（包括 `+=` 等）是语句，不产生值：不能 `a = b = 0`，不能写在条件里（ADR-0011）。
- 优先级从高到低（ADR-0019）：成员访问、调用、下标 → 一元（`-` `+` `~` `not` `await` `launch`）→ `as` → `*` `/` `%` → `+` `-` → `<<` `>>` → `..<` `...` → `<` `>` `<=` `>=` `is` → `==` `!=` → `&` → `^` → `|` → `and` → `or` → `??`。除了 `as`（ADR-0014）和区间，其余同 C#。`??` 从右往左结合，区间不结合，其余从左往右。

## 类型检查与收窄（ADR-0010）

```nyxel
func OnHit(self, other: Collider) {
    if other is Pickup {
        other.Collect()             // 这里 other 是 Pickup
    }
    if other is not Enemy {
        return
    }
    other.TakeDamage(5)             // 这里 other 是 Enemy
}
```

- `x is T` 得到 bool；`x is not T` 是否定。`is` 后面只能写类型。
- 被检查的是局部变量或参数时自动收窄（同空值收窄的规则），不另起变量名。
- 待定：`x is 某个 enum case`。

## 类型转换（ADR-0014）

```nyxel
let cells = self.distance as int                 // 向零截断：2.7 → 2，-2.7 → -2
let percent = self.current as float / self.Max   // 先转换再除，不是整数除法
let index = MathF.Round(self.t * 10) as int      // 要四舍五入就先 Round
let code = Element.Fire as int                   // 枚举和整数互转

let p = other as Projectile                      // 错误：可能失败，用 if other is Projectile
```

- `as` 是转换，不是类型检查，不会因为“类型不对”而失败。允许：数字之间、枚举和整数之间、子类转父类或接口、C# 类型定义的显式转换运算符。
- 可能失败的向下转换写 `as` 是编译错误，用 `is` 收窄。
- 语义同 C# 的强制转换：向零截断，不检查溢出，常量超出范围是编译错误。
- 优先级比 `*` `/` 高，比一元运算符和成员访问低。
- 没有 C# 的 `(int)x` 写法。

## 错误处理（ADR-0014）

```nyxel
try {
    let text = File.ReadAllText(path)
    self.Apply(text)
} catch e: FileNotFoundException {
    Log.Warn($"没有存档：{path}")
} catch e: IOException when e.HResult == 32 {
    Log.Warn("文件被占用")
} finally {
    self.loading = false
}

let config = try { Config.Load(path) } catch e: IOException { Config.Default }
```

- 用 .NET 的异常。`catch 名字: 类型`，名字必须写；捕获所有异常写 `catch e: Exception`。`when` 过滤同 C#。
- 被前面的 `catch` 完全覆盖、永远到不了的 `catch` 是编译错误。`try` 后面至少有一个 `catch` 或 `finally`。
- `} catch`、`} finally` 跟在 `}` 后面同一行。
- `try` 可以当表达式，规则同 `if` / `match`：每个分支最后一个表达式是值，或者以 `return` / `throw` 离开；`finally` 不产生值。
- `throw 异常对象` 是语句。在 `catch e` 里 `throw e` 重新抛出同一个异常，保留原来的调用栈（翻译成 C# 的 `throw;`）。
- `finally` 里不能 `return`。
- 待定：文档注释里写会抛哪些异常的约定。

## 资源释放（ADR-0015）

```nyxel
func ReadSave(self, path: string) -> string {
    using stream = File.OpenRead(path)
    using reader = new StreamReader(stream)
    return reader.ReadToEnd()
}   // 离开这个块时释放：先 reader 后 stream
```

- `using 名字 = 值`：值必须实现 `IDisposable`，在所在块结束时调用它的 `Dispose()`。多个按声明的相反顺序释放；`return`、`break`、异常离开时也一样。值为 `null` 时跳过。
- `using` 自己引入名字，不写 `let`（同 `for x in`、`catch e: T`）。名字不能重新赋值。需要时可以写类型：`using stream: FileStream = ...`。
- `using` 只有这一种用法：导入是 `import`，没有块形式的 `using (...) { }`，没有 `defer`。要更早释放就拆成函数；和 `IDisposable` 无关的清理用 `try` / `finally`。
- 待定：`await using`（`IAsyncDisposable`）。

## 协程（ADR-0016）

```nyxel
import Nyxel                            // Wait 在 Nyxel.Runtime 里

async func Intro(self) {
    self.Camera.Focus(self)
    await Wait.Seconds(2)               // 暂停 2 秒，游戏照常运行
    self.Roar()
    let text = await File.ReadAllTextAsync(self.path)
}

async func Load(self, name: string) -> Level { ... }

func OnHit(self) {
    launch self.FlashRed()              // 启动，不等
}

let job = launch self.Load(name = "Forest")
let level = await job                   // 之后再等结果
```

- `async func` 声明会跨帧的函数，可以有返回值（`-> R`）。lambda 写 `async func(e) { }`，函数类型写 `async func(A) -> R`。重写时 `async` 必须和基类一致。不能有 `out` / `ref` 参数。
- `await` 只能写在 `async` 函数里，能等 Nyxel 的协程、.NET 的 `Task` / `ValueTask`、任何有 `GetAwaiter` 的对象。恢复后的代码在主线程、下一次调度时执行。
- 结果可以等待的调用（`async` 函数、返回 `Task` 的 .NET 方法）只能直接写在 `await` 或 `launch` 后面，否则编译错误。
- `launch 调用` 得到协程的句柄：`job.Stop()` 停止，`await job` 等它完成。
- `launch obj.方法()` 的协程绑定在 `obj` 上：`obj` 被引擎销毁后，下一次恢复时自动停止。静态函数和没有生命周期的对象启动的协程不绑定。
- 停止时 `finally` 和 `using` 的释放照常执行，`catch` 接不住。`await` 一个被停止的协程，等待方也停止。
- `launch` 出去的协程里没被接住的异常交给宿主记日志。
- 热重载前所有协程都会停止。
- 等待函数（Nyxel.Runtime，`Nyxel` 命名空间）：`Wait.Seconds(s)`、`Wait.NextFrame()`、`Wait.Until(func() = 条件)`。
- 待定：同时等多个协程；句柄类型和 `Wait` 系列的最终命名。

## 属性（ADR-0011）

```nyxel
class Player : Behaviour {
    var hp: int = 100                       // 字段

    public property IsDead: bool {          // 计算属性
        get { return self.hp <= 0 }
    }

    public property Hp: int {
        get { return self.hp }
        set(value) { self.hp = Math.Clamp(value, 0, 100) }
    }

    public property Score: int = 0 {        // 自动属性：编译器存值
        get
        private set                         // 外部只读
    }
}

interface IDamageable {
    property Health: int { get }
}
```

- `property` 声明属性；`var` / `let` 只声明字段。
- 访问器要么都带函数体（计算属性），要么都不带（自动属性，初始值写在类型后面）。每个访问器一行，可以单独加可见性。
- setter 的新值在括号里命名：`set(value)`。访问器里 `self` 隐式可用。静态属性写 `static property`。
- 默认用字段；实现接口的属性、对外只读对内可写、值需要计算时用属性。
- 待定：只能在构造时设置的访问器（C# 的 `init`）；索引器；`field` 关键字。

## 事件（ADR-0017）

```nyxel
public class Slime : Behaviour {
    public event Died(slime: Slime)
    public event HealthChanged(before: int, after: int)

    func Die(self) {
        emit self.Died(slime = self)        // 没人订阅时什么也不发生
        self.Destroy()
    }
}

// 在 Spawner 里订阅
slime.Died += self.OnSlimeDied              // 绑定 self：Spawner 销毁后自动移除
slime.HealthChanged += func(before, after) {
    self.hud.Shake()
}
slime.Died -= self.OnSlimeDied              // 提前取消
```

- `event 名字(参数)` 声明事件，参数写法同函数参数，没有返回值和默认值。可以加 `public`、`protected`、`static`，接口里也能声明。
- `emit 接收者.事件(实参)` 触发，只有声明事件的类型能 `emit`（子类也不行）。处理函数按订阅顺序同步执行，异常传给 `emit` 的一方。
- `+=` / `-=` 订阅和取消，C# 和引擎的事件也一样。
- 订阅绑定在“执行谁的代码”上：`对象.方法` 绑定那个对象，lambda 绑定写它的 `self`。绑定对象被引擎销毁后，订阅在下一次触发时自动移除；热重载时脚本做过的所有订阅都会移除。对 lambda 写 `-=` 是编译错误。
- C# 那边是普通的 C# `event`。
- 待定：事件访问器（`add` / `remove`）；一个处理函数抛异常时是否继续通知其他处理函数。

## 状态机（ADR-0017）

没有专门语法：状态用 enum 写，每种状态需要的数据放在 case 里，在 `Update` 里 `match` 当前状态；按顺序推进的流程用协程。

```nyxel
enum SlimeState {
    case Idle
    case Chasing(target: Player)
    case Fleeing(from: Player)
}

match self.state {
    case Idle -> self.LookAround()
    case Chasing(target) -> self.MoveTowards(target = target.Position, dt = dt)
    case Fleeing(from) -> self.MoveAway(from = from.Position, dt = dt)
}
```

- 追击时才有 `target`，不会出现“处于 Idle 却拿着一个过期的 target”。
- 新加一个状态，所有没处理它的 `match` 都会编译报错。

## 可见性（ADR-0006、ADR-0008）

- 成员默认私有；公开写 `public`，只给子类用写 `protected`（ADR-0015），含义与 C# 相同。struct 的成员不能是 `protected`。
- 类型默认只在本程序集可见；给其他程序集用写 `public`。
- 重写时可见性必须和基类一致：基类是 `protected virtual` 就写 `protected override`。
- 没有 `internal`，也没有 C# 的 `protected internal`、`private protected`。
- 修饰符的顺序固定（ADR-0019）：可见性（`public` / `protected` / `private`）→ `static` → `abstract` / `sealed` / `virtual` → `override` → `async`。顺序不对、重复都是编译错误，诊断给出正确的写法：`public static var`、`protected override func`、`public override async func`、`sealed override`。

## 命名（ADR-0006）

编译器检查，不符合给警告。

| 什么 | 写法 | 例 |
|---|---|---|
| 类型、方法、属性、`public` / `protected` 字段、命名 init、enum case | PascalCase | `EnemySpawner`、`TakeDamage`、`SpawnInterval`、`FromPolar`、`Burn` |
| 参数、局部变量、非公开字段 | camelCase，不加前缀 | `amount`、`spawnPoint`、`hp` |

调用 .NET API 时名字原样使用。待定：缩略词的大小写（`Id` / `IO`）。

## 待定

大的主题都讨论过了。剩下的细节记在各节末尾的“待定”里，写 samples 或实现时遇到再逐个讨论。

实现语法分析时碰到、还没讨论的写法。目前都按最保守的方式处理：报编译错误（以后放开不会让已有代码失效），诊断见 design/diagnostics.md：

- 字符字面量 `'a'`（NYX1009）。
- 小数点开头的小数 `.5`（NYX1010，目前要写 `0.5`）。行首的 `.` 是续行，`.5` 放在行首时读起来容易混。
- 数组类型 `T[]` 和数组的创建（NYX1124）；索引 `a[i]` 照常可用。
- 嵌套类型：类型里声明类型（NYX1114）。
- 单独的块语句：语句位置直接写 `{ ... }`（NYX1102）。
- `+=` 等复合赋值写在行尾时是否续行（见“换行与续行”）。
- 逐字字符串 `@"..."`、原始字符串 `"""..."""`（NYX1008，见“字符串”）。
