# ADR-0039: 没有 `self` 的函数写 `static func`、带数据的 enum 写明 `struct enum` 或 `class enum`

- 状态：已接受
- 日期：2026-11-04

## 背景

第三十二轮讨论，language-reference 整体 review 的第二批（第一批是 ADR-0038）。两项都是推翻旧 ADR 里的一个默认。

1. **静态用两种方式表达。** ADR-0008 决策 6：第一个参数是 `self` 的是实例方法，没有 `self` 的是静态函数，“不需要 `static` 关键字”；而字段和属性仍靠 `static var` / `static property`。同一个概念，字段和属性靠关键字，函数靠“缺了什么”，违反 ADR-0003 的“一件事一种写法”；`func Distance(...)` 要扫到括号里才知道能不能用 `self`，而 `static var` 第一眼就知道，违反“看一行的第一个词就知道它声明了什么”。顺带一提，`init`、属性访问器、`operator` 里 `self` 都是隐式的，“`self` 写在参数里”其实只对 `func` 成立。
2. **带数据的 enum 默认在堆上。** ADR-0024 决策 6：`enum` 的值在堆上（抽象基类加每个 case 一个子类），`struct enum` 是值类型。短的那个写法恰好是分配的那个，而 ADR-0003 的准则四是“不为语法便利引入隐藏的分配”。samples 里 Slime 的状态机写的是 `enum`，每次状态切换分配一次；Turret 写的是 `struct enum`；Agent 看到两种会随手写短的。ADR-0024 否决“全都是值类型”的三条理由（大的 case 复制、不能递归、C# 那边不能按子类 `switch`）仍然成立，但它们是“有些 enum 该放堆上”的理由，不是“默认放堆上”的理由。

两项都按推荐定了（第 3 项 B，第 4 项 D）。

## 决策

1. **没有 `self` 的函数必须写 `static func`；有 `self` 的照旧不写**（修改 ADR-0008 决策 6）。
   ```nyxel
   class Slime {
       public static var Killed: int = 0
       public func Die(self) { }
       public static func Distance(a: Vector3, b: Vector3) -> float { }
       static func Main() { }
   }
   ```
   - 两个标记必须一致：`static func F(self)` 是编译错误（有 `self` 就是实例方法，去掉 `static`）；`func F()` 也是编译错误（加 `self` 或者加 `static`）。信息是冗余的，冗余的用处正是漏写一个时能报错，而不是悄悄变成另一种含义。
   - 适用于类、struct、接口、enum 的成员和 `extension` 块里的函数。`operator` 和 `init` 不变：运算符总是静态的，`init` 总是实例的，没有选择就不写标记（ADR-0030、ADR-0008）。
   - `static func` 的修饰符顺序同 ADR-0019：`public static func`、`public static async func`。
   - 程序入口是 `static func Main()`（修改 ADR-0036 的措辞，规则不变）。
   - 这两条都只看声明，由 Parser 报（NYX1237、NYX1238）。
2. **带数据的 enum 必须写明放在哪：`struct enum` 是值类型，`class enum` 在堆上；光写 `enum` 是编译错误**（修改 ADR-0024 决策 6、ADR-0008 决策 2）。
   ```nyxel
   struct enum SlimeState {                 // 值类型：赋值时复制，不分配
       case Idle
       case Chasing(Target: Player)
   }

   class enum Condition {                   // 引用类型：堆上的对象，可以递归
       case Leaf(Check: func() -> bool)
       case Not(Inner: Condition)
       case All(Items: List<Condition>)
   }

   enum Element {                           // 不带数据：照旧，本来就是值类型
       case Fire
       case Ice
   }
   ```
   - 这和 class / struct 的关系一样：类型之间也没有默认，必须写明。读者在声明处就看到存法，Agent 漏写时诊断列出两个选项和各自适合什么（NYX1235）。
   - `struct enum` 的规则不变（ADR-0024 决策 6）：一个值里留着所有 case 的数据的位置，case 不能直接包含同一个 struct enum，全零的值是不带数据的第一个 case，数据全是 unmanaged 时整个也是。不带数据的 enum 只有要实现接口时才写 `struct enum`（ADR-0029）。
   - `class enum`：原来 `enum` 带数据时的一切（抽象基类加每个 case 一个密封子类、`==` 比内容、能实现接口、数组要用 `element` 创建）。递归的 case、数据大的 case 用它。不带数据的 enum 写 `class enum` 是编译错误（NYX1236），诊断给出 `enum`（要实现接口是 `struct enum`）。
   - 文档里“带数据的 enum”仍然泛指两种；只说其中一种时写 `struct enum` 或 `class enum`。
   - C# 那边：`class enum` 同原来的 `enum` 带数据时；`struct enum` 不变。

## 备选方案

- 第 1 项：
  - **维持“没有 `self` 就是静态”**（ADR-0008 原来的决定）：少一个词；代价是背景里的两条不一致，以及 C# 习惯的 Agent 写 `static func` 会被报错。
  - **Swift / C# 式隐式 `self` 加 `static func`**：ADR-0008 已经否决，声明那一行看不出是不是实例方法，struct 修改自己还要另加 `mutating`。本决策不动显式 `self`。
  - **`static` 可写可不写**：同一件事两种写法。
- 第 2 项：
  - **维持 `enum` 在堆上、`struct enum` 是值类型**（F# 的做法，ADR-0024 原来的决定）：短的那个写法会分配。
  - **反过来，`enum` 是值类型、`class enum` 在堆上**（Swift、Rust 的做法）：状态机默认不分配；代价是读者看到 `enum` 要知道它是值类型，赋值时复制，和 C# 程序员对“enum”的直觉有距离。
  - **编译器看 case 的大小和递归自动选**：读的人看不出赋值时是复制还是共用（ADR-0024 已经否决）。
  - **`ref enum` 代替 `class enum`**：`ref` 在 Nyxel 里是参数修饰，含义不同；`class enum` 和 `struct enum` 对称，和 class / struct 的含义一致。

## 后果

- ADR-0008 决策 2、6 和 ADR-0024 决策 6 改成现在的规则，原来的做法挪进备选方案；ADR-0036 的 `Main` 改写成 `static func Main`；ADR-0014、0021、0029、0031、0035 例子里没有 `self` 的函数补上 `static`，ADR-0008、0024、0029 例子里带数据的 `enum` 改成 `struct enum`。
- samples：六个没有 `self` 的函数补上 `static`；`SlimeState`、`Damage` 改成 `struct enum`（每次状态切换、每次受击都不再分配）。语法树快照和高亮快照跟着重新生成。tests/…/Programs 的 `Main`、`Fib`、`Say` 补上 `static`。
- Parser：`class enum` 和 `struct enum` 一样读；新错误码 NYX1235 到 NYX1238（diagnostics.md）。“没有 `self` 的函数写了 `static`”原来留给语义分析，现在是语法错误。Binder 的静态判断仍看 `self`（Parser 保证两者一致），降级不变。
- 高亮：`static` 已经是修饰符，`class` 已经是类型关键字，语法文件不用改。
- 和 C# 的关系：`static func` 和 C# 的 `static` 含义相同；`class enum` 没有 C# 对应物，不会似是而非。
