// 一致性自检：分段表的“鼠标方向”必须与弹道骨架的实际走向相反
const fs = require("fs");
const html = fs.readFileSync("D:/15949/Documents/DSH/ak47-spray/index.html", "utf8");

const PATTERN = eval(html.match(/const PATTERN = (\[[\s\S]*?\]);/)[1]);
const SEGMENTS = eval("(" + html.match(/const SEGMENTS = (\[[\s\S]*?\]);/)[1] + ")");
const DT = 0.1, N = 30;

let bad = 0;
const fail = (m) => { console.log("✗ " + m); bad++; };

if (PATTERN.length !== N) fail("PATTERN 点数 = " + PATTERN.length + "，应为 " + N);
if (SEGMENTS[0].from !== 1) fail("首段不是从第 1 发开始");
if (SEGMENTS[SEGMENTS.length - 1].to !== N) fail("末段不是到第 " + N + " 发结束");
for (let i = 1; i < SEGMENTS.length; i++) {
  if (SEGMENTS[i].from !== SEGMENTS[i - 1].to + 1) fail("分段不连续：" + JSON.stringify(SEGMENTS[i - 1]) + " -> " + JSON.stringify(SEGMENTS[i]));
}

console.log("段 | 发数 | 时间(s) | 弹道净位移 dx,dy | 应为方向 | 表中方向");
for (const s of SEGMENTS) {
  const a = PATTERN[s.from - 1], b = PATTERN[s.to - 1];
  const dx = b[0] - a[0], dy = b[1] - a[1];
  const ratio = Math.abs(dx) / Math.abs(dy);
  let expect = "down";
  if (ratio >= 0.15) expect = dx > 0 ? "downLeft" : "downRight"; // 弹道右摆 -> 鼠标左拉
  const ok = expect === s.dir;
  if (!ok) fail(`第 ${s.from}-${s.to} 发：弹道 dx=${dx.toFixed(1)} dy=${dy.toFixed(1)} 期望 ${expect}，表中 ${s.dir}`);
  console.log(
    String(SEGMENTS.indexOf(s) + 1).padStart(2) + " | " +
    String(s.from + "-" + s.to).padEnd(5) + " | " +
    ((s.from - 1) * DT).toFixed(2) + "-" + (s.to * DT).toFixed(2) + " | " +
    ("dx=" + dx.toFixed(1) + ",dy=" + dy.toFixed(1)).padEnd(18) + " | " +
    expect.padEnd(9) + " | " + s.dir + (ok ? "  ✓" : "  ✗")
  );
}

// 时间轴覆盖
const total = N * DT;
console.log("\n总时长 " + total.toFixed(1) + "s，最后一段结束于 " + ((SEGMENTS[SEGMENTS.length - 1].to) * DT).toFixed(2) + "s");
console.log(bad === 0 ? "\n全部检查通过 ✓" : `\n发现 ${bad} 处问题 ✗`);
process.exit(bad === 0 ? 0 : 1);
