# ADR-0008: 类型定义：class / struct / enum、命名构造、new、self

- 状态：已接受
- 日期：2026-10-02

## 背景

第四轮讨论：类型怎么定义、怎么构造、方法里怎么访问自己。和用户逐项对比代码后选定。构造函数这一项，用户提出了两个想法：构造函数能不能有名字；构造函数、成员函数、静态函数是否可以靠参数列表区分（把 `self` 写进参数）。讨论后采纳了命名构造和显式 `self` 参数，构造函数仍保留 `init`（理由见备选方案）。之后用户看示例时问 `var self` 有没有用（Spawner 是 class，方法不写 `var self` 也在改字段）；解释了 class / struct 的区别后保留，并补全了 `let` 的 struct 和临时副本的规则（决策 6）。

## 决策

1. **class、struct、interface 与 C# 相同**：class 是引用类型，struct 是值类型。继承和实现接口写 `class Slime : Behaviour, IDamageable`。`virtual` / `override` / `abstract` / `sealed` 的用词和含义同 C#。接口名按 .NET 惯例以 `I` 开头。类型默认只在本程序集可见，给其他程序集用写 `public`。
2. **枚举统一用 `enum`，每种情况一行，以 `case` 开头**。case 可以带数据，数据的每一项必须有名字：
   ```nyxel
   enum Element {
       case Fire
       case Ice
   }
   enum Damage {
       case Physical(amount: int)
       case Burn(amount: int, seconds: float)
   }
   ```
   所有 case 都不带数据时翻译成 C# 的普通枚举；只要有一个 case 带数据，就翻译成抽象基类 + 每个 case 一个密封子类。带数据的 case 的值是堆上的对象。
3. **构造函数是 `init`**：
   - 最多一个不带名字的 `init`；其余必须有名字：`init FromPolar(radius: float, angle: float)`。构造函数不靠参数类型重载区分。
   - **确定初始化检查**：没有默认值的字段，在每个 `init` 结束前都必须赋过值，否则编译错误。`let` 字段只能在声明处或 `init` 里赋值。
   - `init` 不写 `public` 就是私有的（同 C#，与 ADR-0006 一致）。
   - `init` 里的 `self` 是正在构造的对象，不写在参数列表里。
   - 翻译：不带名字的 `init` 是 C# 构造函数；带名字的 `init` 是一个私有构造函数（带一个隐藏参数，避免和别的构造函数签名冲突），加一个同名的公开静态方法。所以 C# 那边看到的是 `Point.FromPolar(1, 0.5)`。
4. **创建值一律写 `new`**：`new Slime(maxHp = 30)`、`new Point.FromPolar(radius = 1, angle = 0.5)`、`new Damage.Burn(amount = 12, seconds = 3)`（命名实参的写法见 ADR-0011）、`new List<Enemy>()`。不带数据的 case 是常量，不写 `new`：`Element.Fire`。不带 `new` 的 `名字(...)` 一定是函数调用。
5. **成员一律通过接收者访问**：实例成员写 `self.hp`、`self.Die()`；静态成员写 `Slime.AliveCount`、`Slime.Count()`。不带接收者的名字只能是局部变量或参数。私有字段不加前缀。直接写字段名是编译错误，诊断给出 `self.` 或 `类型名.` 的写法。
6. **实例方法和静态函数靠参数列表区分**：
   - 第一个参数是 `self`（不写类型）的是实例方法，没有 `self` 的是静态函数，不需要 `static` 关键字。
   - struct 的方法要修改自己时写 `var self`；只写 `self` 的 struct 方法不能修改自己的字段，也不能调用自己的 `var self` 方法。
   - `var self` 只能用在 struct 上。class 方法的 `self` 是引用：通过它总能修改对象的字段，而 `self` 本身不会指向别的对象，“方法改不改自己”对 class 没有意义。class 方法写 `var self` 是编译错误，诊断要说明这个原因。
   - **`let` 的 struct 整个不可变**：struct 变量存的是数据本身，所以 `let` 的 struct 局部变量或字段，既不能给它的字段赋值，也不能调用它的 `var self` 方法。
   - **不能在临时副本上调用 `var self` 方法**：属性、索引器、函数调用取出来的 struct 是一份副本，修改会随副本丢失（C# 允许这样写，Unity 的 `transform.position.Normalize()` 就是这样悄悄失效的）。这是编译错误，诊断建议先存进 `var` 局部变量，改完再写回。
   - 调用时不传 `self`：`slime.TakeDamage(5)`；静态函数只能通过类型名调用：`Slime.Count()`。不能写成 `Slime.TakeDamage(slime, 5)`。
   - 字段和属性没有参数列表，仍用 `static` 标记静态的。
   - 翻译：没有 `self` 是 C# 的 static 方法；class 的 `self` 方法是实例方法；struct 的 `self` 方法是 C# 的 `readonly` 实例方法（顺带避免防御性复制），`var self` 方法是普通实例方法。

## 备选方案

- **带数据的枚举另用 `union` 关键字**：区分更显眼，但 C / C++ 的 `union` 是“共享同一块内存”，C# 正在设计的 union 又是另一种含义，有似是而非的风险。**用 Kotlin 的 sealed class 层次**：最接近生成的 C#，但写出来是一组类，读者要自己看出“这是几选一”。
- **主构造函数**（Kotlin、C# 12）：最短，但 C# 的主构造函数参数会被捕获成隐藏的可变字段，参数在哪里可见很微妙。**`constructor`**（TypeScript）：除了更长，与 `init` 相同。
- **构造函数按参数类型重载**（C#）：`new Color(1, 0, 0)` 调的是 float 版还是 byte 版，要看字面量定型才知道；参数类型相同的两种构造（直角坐标 / 极坐标）在 C# 里根本写不出来。**不支持命名构造，额外的创建方式写成静态工厂函数**：不需要新语法，但调用处没有 `new`，和“`new` 表示创建值”的规则不一致；命名构造的写法 `new Point.FromPolar(...)` 正好和 `new Damage.Burn(...)` 同形。
- **构造函数也做成普通函数**（Rust：静态函数返回一个列出全部字段的字面量）：概念最少，但构造函数在 CLR 里确实特殊：构造过程中对象还不完整；`let`（只读）字段只能在构造函数里赋值；子类构造必须先构造基类，而 Nyxel 的脚本类大多继承引擎的 C# 基类；C# 的 `new`、引擎用反射创建对象、序列化库都要求真正的 CLR 构造函数。Rust 能这样做，是因为它没有继承，也不和 C# 互操作。
- **创建对象不写 `new`**（Swift、Kotlin）：更简洁，但它们的方法名小写开头，`Vector3(...)` 一看就是类型；Nyxel 沿用 .NET 命名，方法名也是大写开头，`Spawner(...)` 和 `Spawn(...)` 写法相同。
- **不写 `self`，私有字段加 `_` 前缀**（.NET 运行时代码的约定）：靠前缀区分字段和局部变量，但方法调用和静态成员仍然分不清来源。**都不要**（C# 默认）：最简洁，但 `hp` 是字段还是局部变量要往上找，同名时容易写出 `hp = hp`。用 `this` 代替 `self`：含义相同，选 `self` 是为了和 Swift 一致，并和显式 `self` 参数配合。
- **Swift / C# 式隐式 `self` + `static func`**：大家熟悉，但声明那一行看不出是不是实例方法，struct 修改自己还要另加 `mutating` 关键字；显式 `self` 参数用 `var self` 就表达了，复用了 `let` / `var` 的含义。
- struct 修改自己的其他表达方式：
  - **class 方法修改字段也写 `var self`**：形式上统一，但 class 的对象可能被多个变量共享，这个标记挡不住通过别的变量修改，实际只是注释；C# 和引擎的 class 没有这种标记，无法检查；还会像 C++ 的 `const` 一样层层传染（重写引擎的 `Update` 几乎都要写，不带 `var` 的方法也不能调用带 `var` 的）。
  - **编译器看函数体自动推断**：写起来省事，但签名上看不出来，读者和 Agent 必须读函数体；改了函数体，别处的调用可能突然编译不过。违反 ADR-0003 的“边界处显式”。
  - **Nyxel 的 struct 一律不可变**（要“改”就返回新值再赋回去）：`var self` 整个消失，但计时器、冷却这类常修改的小 struct 写起来更绕；.NET 的 struct 照样可变，`let` 的规则还是需要。
  - **同 C#，struct 方法默认都能修改**：不用标记，但编译器不知道哪些方法会修改，只能在 `let` 和临时副本上悄悄复制一份再调用，修改丢失时不报错。

## 后果

- 一个名字的来源在使用处就看得出：没有前缀是局部变量或参数，`self.` 是实例成员，`类型名.` 是静态成员。
- 写 C# 习惯的 Agent 会漏写 `self.` 和 `self` 参数、漏写或多写 `new`，都会得到编译错误和明确的修改提示。
- class 方法不写 `var self` 也能改字段，struct 方法却要写，读者可能以为漏写了（用户看示例时就问过）。靠语言参考里 class / struct 的说明和诊断信息解释。
- 待定：C# 里没有标 `readonly` 的 struct 方法（可能修改，也可能只是没标）在 `let` 和临时副本上怎么处理。全当 `var self` 会让没标注的引擎 struct 很难用；像 C# 那样悄悄复制又会漏掉修改丢失。实现 .NET 互操作时再定。
- 写在类型外面、参数是 `self: 类型` 的函数天然可以表达扩展方法（对应 C# 的 `this` 参数）。后来扩展定为 `extension` 块（ADR-0014），没有采用这种写法。
- 后来定了：调用基类 `init`、没有 `init` 时的默认 `init`（ADR-0013）；属性（ADR-0011）；访问 case 数据（ADR-0009）。
- 待定：简单枚举的底层值和 flags；带数据枚举的值类型版本（避免分配）。
