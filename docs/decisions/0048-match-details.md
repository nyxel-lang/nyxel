# ADR-0048: match 的细节：到不了的分支是错误，`bool` 列全就够，case 只写名字，取出的数据是 `let`，当值用时的类型，空值收窄

- 状态：已接受
- 日期：2026-11-12

## 背景

第四十轮讨论。M2 实现 `match` 时，有六处 ADR 没写到、或者和实现冲突的细节，先按最严做了，列给用户确认：

1. 前面的分支已经覆盖了的分支怎么办。C# 对这样的分支报错误（switch 表达式的 CS8510、switch 语句的 CS8120），生成的 C# 里只要有一个，就会变成“编译器内部错误”。
2. ADR-0009 决策 7 说“对其他类型必须有 `case _`”，`bool` 也算其他类型；可 `true`、`false` 之后的 `case _` 正是第 1 项说的分支。
3. `case Element.Fire` 这种带类型名的写法：ADR-0024 的例子都只写名字（`case Fire`），没说带类型名行不行。
4. 取出的 case 数据能不能赋值：ADR-0009 决策 6、ADR-0024 决策 8 只说它们是“分支里的局部变量”。
5. 当值用的 `match` 是什么类型：ADR-0025 说“各分支的共同类型，有期望的类型时按期望的类型”，没说 `null` 分支和数字分支怎么算。
6. 收窄：ADR-0010 决策 7 只写了 `case is T` 在自己的分支里收窄。实现时 `case not null` 也收窄了，`case null -> return` 之后的分支却不收窄。

用户先问了第 6 项为什么后面的分支不收窄：实现时取了最保守的做法，理由是“收窄只来自 ADR 列的写法”，而且收窄少一点只会让人多写，不会放过有问题的代码，以后放宽也不会让已有的代码编不过。对比了和 `if x == null { return }`、C# 可空分析的一致性后，用户选了也收窄。其余五项用户确认按实现的做法。

## 决策

1. **到不了的分支是编译错误**，同 C#：前面没有 `when` 的分支已经覆盖了它的每一个值。

   ```nyxel
   match element {
       case Lightning -> ...
       case Spark -> ...           // 错误：Spark 是 Lightning 的别名（ADR-0045），和第 2 行同一个值
       case _ -> ...
   }

   match n {
       case _ -> ...
       case 0 -> ...               // 错误：case _ 已经接住了所有值
   }
   ```

   - 诊断说明是哪一行已经覆盖了它。
   - 同一个分支里 `or` 的两边是同一个值（`case Fire or Fire`、.NET 枚举的两个别名）也报，同 ADR-0040 决策 3。
   - `bool` 列出 `true`、`false`，`struct enum` 列全 case 之后，`case _` 到不了。
   - 不带数据的 enum 列全 case 之后，`case _` 还能接住不是任何 case 的值（`5 as Element`），不算到不了。`class enum` 是引用类型，列全 case 之后的 `case _` 还能接住 C# 代码传进来的 null，也不算。
   - 区间一个值也没有时同样报（`case 5..<5`）。
2. **`bool` 列出 `true` 和 `false` 就覆盖全了**，不用写 `case _`。修改 ADR-0009 决策 7 的“对其他类型必须有 `case _`”。
3. **被匹配的 enum 自己的 case 只写名字**：`case Fire`；写成 `case Element.Fire` 是编译错误，诊断给出 `case Fire`。
   - 同一个值只留一种写法，enum 是什么从被匹配的值就看得出来。
   - 别的类型里的常量照常写类型名：`case Limits.Max`（ADR-0038）。
4. **取出的 case 数据是 `let`**：`case Burn(amount, _) -> amount = 1` 是编译错误。模式里没有写 `let` / `var` 的位置（解构写得出来，ADR-0021），按不能改处理；要改就先存进一个 `var`。
5. **当值用的 `match` 的类型**：
   - 有期望的类型（`let x: float = match ...`、参数、返回值），而且每个分支都能转成它时，就是它。
   - 否则是各分支的共同类型：其余分支都能隐式转成的那一个。有一个分支是 `null` 时是它的 `T?`（`"zero"` 和 `null` 是 `string?`）。没有共同类型是编译错误，诊断建议写明类型或者用 `as` 转换。
   - 只有数字的分支跟着有类型的分支走（ADR-0013 的“字面量跟着旁边有类型的值”）：`case Burn(_, seconds) -> seconds` 加 `case _ -> 0`，`0` 是 `float`。全是数字时按期望的类型，没有就用默认的 `int` / `double`，`1` 和 `2.5` 是 `double`。
   - 离开的分支（`throw`、`return` 等）不参与。
6. **空值收窄**：
   - `case not null` 在自己的分支里收窄，同 `if x != null`：被匹配的局部变量或参数在这个分支里（含 `when`）当作非空。
   - 一个没有 `when` 的分支匹配了 null 之后，后面的分支里被匹配的值当作非空；`match` 之后从这些分支走出来的路径上也是，同 `if x == null { return }`：

     ```nyxel
     match target {                  // target: Enemy?
         case null -> return
         case _ -> target.Attack()   // target 是 Enemy
     }

     match target {
         case null -> return
         case _ -> {}
     }
     target.Attack()                 // 同样是 Enemy：唯一能走到这里的分支在 null 之后
     ```

   - “匹配了 null”指模式是 `case null`，或者 `or` 的一边是 `null`（`case null or Fire`）。
   - 带 `when` 的分支不算：`case null when flag -> return` 不保证排除了 null。这和第 1 项判断覆盖时的规则一样。
   - 只管 null，不管类型：`case is Slime -> ...` 之后，`case _` 里的值不会变成“不是 Slime”。Nyxel 没有这样的类型，`if x is Slime {} else {}` 的 `else` 里同样不收窄。
   - 其余同别的收窄：只对局部变量和参数（ADR-0009），被 lambda 捕获的 `var` 不收窄（ADR-0042）。

## 备选方案

- 第 1 项：
  - **到不了的分支只给警告**：C# 那边是错误，生成的 C# 就编不过，只能在降级时把这样的分支删掉或者改写；写的人多半是写错了（列了两次、`case _` 放在了前面），警告容易被忽略。
  - **照写，交给 C# 报**：消息是 C# 的术语，指向生成的代码，按内部错误报出来。
- 第 2 项：**`bool` 也必须写 `case _`**（ADR-0009 原来的字面意思）：和第 1 项冲突，`case _` 一个值也接不到。
- 第 3 项：**两种写法都行**：`case Element.Fire` 和 `case Fire` 是同一个分支，读的人要知道它们一样，Agent 写出来的风格也不统一。
- 第 4 项：**同 C# 的模式变量，可以赋值**：分支里改了它，读的人会以为改到了 case 的数据；case 的数据创建以后本来就不能改（ADR-0024）。
- 第 5 项：**不看期望的类型，只看各分支**（C# switch 表达式的“自然类型”优先）：`let x: float = match ... { case A -> 1  case _ -> 2.5 }` 就成了 `double`，再转 `float` 报错，和 `let x: float = 2.5` 不一致。
- 第 6 项：
  - **只有分支自己的模式收窄**（实现时的做法）：后面的分支要写 `case not null` 才能用。不选的理由：
    - `case null -> return` 和 `if target == null { return }` 是同一个意思，在 `if` 里收窄、在 `match` 里不收窄，就成了一条要特意记住的例外。
    - C# 的可空分析对 switch 也是这样：前面处理过 null，后面的分支就当作非空。从 C# 来的人会默认它能用。
    - 代价只是读 `case _ -> target.Attack()` 时要往上看一眼才知道为什么不是 null，而 `if x == null { return }` 之后的代码本来也是这样读的。
  - **也按类型排除**（`case is Slime` 之后当作“不是 Slime”）：要给类型系统加“排除了某个类型”的类型，用处很少，`if` 里也没有。

## 后果

- 修改了 ADR-0009 决策 7（`bool` 不用 `case _`），原文已改成最新，见它的修订记录。第 6 项补充了 ADR-0009 决策 2 和 ADR-0010 决策 7 列的收窄写法，没有改动原来的规则。
- 错误码：NYX2091（没覆盖全）、NYX2092（到不了）、NYX2096（`case Element.Fire`）、NYX2097（没有共同类型），见 design/diagnostics.md。
- 实现（semantic-analysis.md“match”）：`CheckArms` 按顺序算覆盖；比较、区间、`and`、`not` 这些 Binder 不算覆盖关系的模式，降级时放进 C# 的 `when`，C# 也不看（lowering.md“match”）。所以 `case < 10` 之后的 `case 5` 现在不报到不了，以后要补上就得把这几种的覆盖也算出来。`MethodBinder.AfterArm` 给下一个分支算开始时的状态；`match` 之后的状态照旧是能走到结尾的分支的交集，所以第 6 项的“`match` 之后”自然成立。
