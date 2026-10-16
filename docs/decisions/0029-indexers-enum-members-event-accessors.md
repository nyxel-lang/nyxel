# ADR-0029: 索引器 `property self[...]`、enum 里写成员和实现接口、不加 `field` 和事件访问器

- 状态：已接受
- 日期：2026-10-16

## 背景

第二十二轮讨论，“待定”的第五轮第二批：成员声明。留下来的几项：

- 索引器（ADR-0011）：Nyxel 已经能用 .NET 的索引器（`list[0]`），自己的类型还不能声明。
- `field` 关键字（ADR-0011）：C# 14 的自动属性可以只给部分访问器写体，体里用 `field` 读写编译器生成的后备字段。
- 事件访问器 `add` / `remove`（ADR-0017）。
- 带数据的 enum 实现接口；在 enum 里写方法（ADR-0024，现在用扩展）。

查了 EnginePlayground：自己声明的索引器、`field`、事件访问器、给 enum 写的扩展方法一处都没有，事件全是 `public event Action? Changed;`。这一批更多是补全，不是急需。

C# 那边的几件事用 Roslyn 5.9（C# 14）验证过：

- C# 的 enum 不能实现接口（CS1008）。
- extension 块能给 enum 加实例方法、属性和静态成员，C# 里能写 `element.Weakness()`、`Element.Parse("ice")`。
- extension 块里的索引器还是 preview 功能，C# 14 不能用。

五项（enum 拆成写成员、实现接口两项）对比了代码。前四项按推荐定。第五项原推荐“不带数据的 enum 不能实现接口”，用户提出换成 struct 来实现：比较了“写了接口就自动换成 struct”和“明确写 `struct enum`”，选了后者。

讨论中用户还问了两件事：

- C# 那边指什么？指的是引用 Nyxel 编出的程序集、手写的 C# 代码（引擎和编辑器那边的 C#、序列化器、其他项目），不是 Nyxel 翻译生成的 C#。生成的 C# 由编译器控制，Nyxel 自己的代码不受这些限制。被 C# 当库用早已是目标：ADR-0003 的第 3 条“调用 C# 库、被 C# 调用都不需要胶水代码”，ADR-0004 的“双向互操作”，roadmap M3 的“C# 调用 Nyxel 编出的程序集”。
- 编译期常量的问题能不能给 struct enum 生成一个内嵌的 C# enum 来解决？能解决一部分，见“后果”。

## 决策

1. **索引器写成属性，名字的位置写 `self[参数]`。**

   ```nyxel
   public class Grid<T> {
       let width: int
       var cells: array<T>
       ...
       public property self[x: int, y: int]: T {
           get = self.cells[y * self.width + x]
           set(value) { self.cells[y * self.width + x] = value }
       }
   }

   grid[3, 4] = Tile.Wall
   let t = grid[0, 0]
   ```

   - 访问器的规则同属性（ADR-0011）：`get` 可以写 `= 表达式`，`set(value)` 只能写块，可以 `private set`，只写 `get` 是只读的。
   - 访问器必须带体，没有“自动索引器”：值不知道存在哪里。也不能写初始值（NYX1202）。
   - 参数写法同函数参数，至少一个（NYX1201）。不能有默认值，不能是 `out` / `ref`（NYX1203；C# 也不允许 `out` / `ref`）。
   - 一个类型最多一个索引器，同“自己声明的函数不能重载”（ADR-0011）。例外同函数：实现接口或重写时要求的，比如同时实现 `IList<T>` 和 `IDictionary<string, T>`。
   - 接口里写 `property self[index: int]: T { get }`。
   - 不能 `static`（C# 没有静态索引器）。扩展里暂时不能声明：C# 14 的 extension 块不支持，C# 正式支持后再放开（NYX1202）。
   - 翻译：C# 的索引器 `public T this[int x, int y] { get ...; set ... }`。

2. **不加 `field` 关键字。** 访问器仍然要么都带体，要么都不带。要在 setter 里加检查，就自己写字段加计算属性：

   ```nyxel
   var hp: int = 100

   public property Hp: int {
       get = self.hp
       set(value) { self.hp = Math.Clamp(value, 0, 100) }
   }
   ```

3. **事件没有访问器（`add` / `remove`）。**
   - 要转发别的对象的事件，就订阅它，再 `emit` 自己的事件：

     ```nyxel
     public class HealthBar {
         public event Clicked()
         let button: Button

         public init(button: Button) {
             self.button = button
             self.button.Clicked += self.OnButtonClicked
         }

         func OnButtonClicked(self) {
             emit self.Clicked()
         }
     }
     ```

   - 事件后面写 `{ ... }` 是编译错误（NYX1204），诊断给出上面的写法。
   - 实现接口里的事件、重写基类的事件时，事件的委托类型用接口或基类规定的那个，不再生成新的（ADR-0017 决策 1 的翻译）。参数的类型和顺序必须和那个委托一致，名字自己起：

     ```nyxel
     public class Inventory : INotifyPropertyChanged {
         public event PropertyChanged(sender: object?, e: PropertyChangedEventArgs)

         func Notify(self, name: string) {
             emit self.PropertyChanged(sender = self, e = new PropertyChangedEventArgs(name))
         }
     }
     ```

     C# 那边是 `public event PropertyChangedEventHandler? PropertyChanged;`。Nyxel 自己的接口里声明的事件也一样，用接口生成的那个委托类型。
   - 顺带：事件的参数不能有默认值（ADR-0017）现在由 Parser 检查（NYX1203）。

4. **enum 的 case 后面可以写方法、静态函数和计算属性。**

   ```nyxel
   enum Element {
       case Fire
       case Ice
       case Lightning

       public func Weakness(self) -> Element = match self {
           case Fire -> Element.Ice
           case Ice -> Element.Lightning
           case Lightning -> Element.Fire
       }

       public property IsHot: bool { get = self == Element.Fire }

       public func Parse(text: string) -> Element? { ... }     // 没有 self：静态函数
   }

   let w = element.Weakness()
   let e = Element.Parse("ice")
   ```

   - 先写全部 case，再写成员。case 写在成员后面是编译错误（NYX1205）。
   - 能写：实例方法、静态函数、计算属性（含 `static property`）。不能写：字段、`init`、自动属性、事件、索引器（NYX1206）。enum 的值就是它的 case（加上 case 的数据），没有别的存储。
   - 不能写 `var self`（NYX1208）：enum 的值创建以后不能改（ADR-0024）。
   - 不能写 `protected`、`virtual`、`abstract`、`sealed`、`override`（NYX1207）：enum 不能被继承。
   - 四种 enum（普通、flags、带数据、`struct enum`）都一样。
   - 扩展照样能给 enum 加方法，用于不是自己定义的 enum（C# 和引擎的）。
   - 翻译：
     - 不带数据的 enum 是 C# 的 enum，里面不能有方法。成员放进同一命名空间里生成的 `static class 名字Extensions` 的 extension 块，C# 代码照样写 `element.Weakness()`、`Element.Parse("ice")`。
     - 带数据的 enum：放在抽象基类上（ADR-0008）。
     - `struct enum`：放在生成的 struct 上。

5. **带数据的 enum 和 `struct enum` 能实现接口；不带数据的 enum 要实现接口，就写成 `struct enum`。** 修改 ADR-0024 决策 6 的“不带数据的 enum 写 `struct enum` 是编译错误”。

   ```nyxel
   enum Damage : IDescribable {
       case Physical(Amount: int)
       case Burn(Amount: int, Seconds: float)
       case Heal(Amount: int)

       public func Describe(self) -> string = match self {
           case Physical(amount) -> $"{amount} 点物理伤害"
           case Burn(amount, seconds) -> $"{seconds} 秒内 {amount} 点灼烧"
           case Heal(amount) -> $"治疗 {amount}"
       }
   }

   struct enum Element : IDescribable {      // 不带数据：要实现接口就写 struct enum
       case Fire
       case Ice

       public func Describe(self) -> string = ...
   }
   ```

   - 接口的成员写在 enum 里（决策 4）。
   - 冒号后面：不带数据的 `enum` 是底层类型（`enum Tile : byte`），带数据的 enum 和 `struct enum` 是接口。同 C#：class 冒号后面是基类和接口，enum 冒号后面是底层类型。
   - 不带数据的 `enum` 写接口是编译错误（NYX1209），诊断给出 `struct enum`。它翻译成 C# 的 enum，而 C# 的 enum 不能实现接口。
   - 不带数据的 `struct enum` 只用来实现接口：不带数据、也没写接口时仍是编译错误（NYX1179），诊断给出 `enum`。
   - `struct enum` 不能写底层类型、case 的值和 `flags`，带不带数据都一样（NYX1176）。
   - 带数据的 enum 只能实现接口，不能继承类：它的基类是编译器生成的。
   - `struct enum` 当接口用时会装箱（复制一份到堆上），同 struct。
   - 不带数据的 `struct enum` 在 Nyxel 里的用法和 enum 一样（`Element.Fire`、`match`、`is`、`==`），大小也一样，全零的值是第一个 case。代价在 C# 那边和常量上：
     - 手写的 C# 看到的是 struct，不是 enum：不能把 `Element.Fire` 写在 `switch` 的 case 标签里，不能用 `Enum.GetValues` / `Enum.Parse`，不能 `(int)e`。引擎编辑器、序列化器靠反射认 enum（下拉框、存成名字或数字），也认不出它。
     - `Element.Fire` 不是编译期常量，不能当参数的默认值。C# 里 struct 参数的默认值只能是 `default`，所以至多全零的第一个 case 可以，M2 再定。
     - 要写值（存档、和引擎对齐）就只能用普通 enum。

## 备选方案

- 索引器：
  - **Swift 的 `subscript(x: int, y: int) -> T { ... }`**：多一个关键字，返回类型用 `->`，和属性的 `:` 不一致。
  - **Kotlin 的两个函数 `operator func get(self, ...)` / `operator func set(self, ..., value: T)`**：一个东西拆成两处声明，两处可能写得对不上，ADR-0011 因为同样的理由没选分开的 get / set；还要多一个 `operator`。
  - 选 `property self[...]`：索引器在 C# 里本来就是属性，属性的规则全部照搬；声明 `self[x, y]` 和使用 `grid[x, y]` 同形，对应 C# 的 `this[...]`。
- **`field`：同 C# 14，`property Hp: int = 100 { get; set(value) { field = ... } }`**。好处是少起一个名字，类里的其他方法也没法绕过 setter 直接改值。不选的理由：
  - `field` 是凭空出现的名字，和 ADR-0011 不要隐式的 `value`、ADR-0008 “不带接收者的名字只能是局部变量或参数”的方向相反。
  - 要放开“访问器要么都带体要么都不带”，读者要多分辨一种属性。
  - 省下的主要是打字，ADR-0003 里排最后。
  - C# 14 才有，资料少，Agent 容易写错；局部变量叫 `field` 很常见（EnginePlayground 里就有 `var field = FindField(...)`），当关键字会冲突。
- **事件访问器：同 C#，`event Clicked() { add(handler) { ... } remove(handler) { ... } }`**。和 ADR-0017 的自动绑定冲突：`+=` 交给 `add` 的是编译器生成的包装函数（检查绑定对象是否还活着），再转手 `+=` 给别的事件时，绑定和热重载清理登记在哪一层都要另外定义。用途少，转发用决策 3 的写法，`emit` 和自动绑定照常。
- **enum 的方法：保持现状，只用扩展**。规则不变，但 enum 的定义和行为分在两处，读的人要另外去找。Swift、Kotlin、Java、Rust（`impl`）都能在 enum 上写方法，Agent 也会这么写。
- 实现接口：
  - **不带数据的 enum 不能实现接口**（原推荐）：C# 那边永远是真正的 enum，但要实现接口只能包一层别的类型。用户提出换成 struct 来实现。
  - **普通 `enum` 写了接口就自动翻译成 struct**（用户提的另一种）：不用改关键字。但只加一个接口，C# 那边的类型就从 enum 变成 struct，手写的 C# 里的 `switch`、`(int)e` 全部编译不过，读 Nyxel 代码看不出来，违背 ADR-0003 的第 1 条。
  - **`struct enum` 不加限制，不带数据、不实现接口也能写**：规则少一条，但同一个简单 enum 有了两种写法，写成 `struct enum` 只会白白丢掉 C# enum 和常量。保留 ADR-0024 原来的诊断，还能拦住“以为放进 ECS 组件要写 struct enum”的误用（不带数据的 enum 本来就是值类型）。代价是去掉接口时要把 `struct enum` 改回 `enum`，编译器会提示。
  - 选“明确写 `struct enum`”：`struct enum` 本来就表示“C# 那边是 Nyxel 生成的 struct”，会改变 C# 形状的选择写在源码里。

## 后果

- 修改了 ADR-0024：决策 6 不带数据的 `struct enum`（原来一律是编译错误，现在实现接口时可以写），决策 1、2 的“带数据的 enum 不能写值、底层类型”扩大到 `struct enum`；ADR-0017 决策 1 的翻译加了实现 / 重写时用对方的委托类型。原文都已改成最新，见各自的修订记录。
- 语法树：
  - `IndexerDeclarationSyntax`（`property`、`self`、参数列表、类型、访问器列表），参数列表是 `BracketedParameterListSyntax`（同 Roslyn）。
  - `EnumDeclarationSyntax.Members`：case 后面的成员。写在成员后面的 case 报 NYX1205，作为跳过的记号留在树里，树里的顺序始终是先 case 后成员。
  - 事件后面的 `{ ... }` 报 NYX1204 后整个跳过；索引器的初始值报 NYX1202 后跳过。
- 新错误码 NYX1201–NYX1209；NYX1179、NYX1176 的消息改成包括实现接口的 `struct enum`。
- 留给语义分析（M2）：
  - 索引器：访问器带体（接口里不带）、一个类型最多一个。
  - 带数据的 enum 和 `struct enum` 冒号后面都是接口。
  - 实现 / 重写事件时参数和委托一致。
  - `field` 当名字用时（找不到这个名字）提示决策 2 的写法。
  - 不带数据的 `struct enum` 当参数默认值。
- 留给降级（M2 / M3）：enum 成员的翻译（extension 块、基类、struct）；C# 那边怎么判断一个 enum 值是哪个 case、怎么取数据（ADR-0024 留下的）。候选方向（用户提议）：给带数据的 enum 和 `struct enum` 生成一个内嵌的 C# enum 列出所有 case，再加一个属性返回当前是哪个 case，手写的 C# 就能 `switch (e.Case) { case Element.Kind.Fire: ... }`，也能用 `Enum.GetValues`、转成整数。它解决不了 `Element.Fire` 本身不是常量（参数默认值、特性的参数），也不会让编辑器和序列化器把 `struct enum` 当成 enum。
- 待定：extension 里的索引器（等 C# 支持）；带数据 enum 的 `ToString`。
