#!/usr/bin/env python3
"""
彩票历史数据爬取脚本
支持双色球(SSQ)和大乐透(DLT)
自动检测系统代理设置
"""

import json
import time
import sys
import os
import argparse
from datetime import datetime, timedelta
from typing import List, Dict, Optional
import urllib.request
import urllib.parse
import ssl

# 忽略SSL验证（某些环境下需要）
ssl._create_default_https_context = ssl._create_unverified_context

# 自动检测代理设置
def get_proxy():
    """从环境变量或系统注册表获取代理"""
    # 1. 检查环境变量
    for key in ['HTTPS_PROXY', 'https_proxy', 'HTTP_PROXY', 'http_proxy']:
        val = os.environ.get(key)
        if val:
            return {'http': val, 'https': val}
    
    # 2. 检查常见本地代理端口（clash/v2ray 默认端口）
    import socket
    for port in [7890, 1080, 10809, 33210]:
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            s.settimeout(0.3)
            result = s.connect_ex(('127.0.0.1', port))
            s.close()
            if result == 0:
                return {'http': f'http://127.0.0.1:{port}', 'https': f'http://127.0.0.1:{port}'}
        except:
            pass
    return None

PROXIES = get_proxy()
if PROXIES:
    print(f"检测到本地代理: {PROXIES.get('https', 'N/A')}")

# 数据源配置
SSQ_API_URL = "https://www.cwl.gov.cn/cwl_admin/front/cwlkj/search/kjxx/findDrawNotice"
DLT_API_URL = "https://webapi.sporttery.cn/gateway/lottery/getHistoryPageListV1.qry"

# User-Agent 池
USER_AGENTS = [
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.0.0",
]


def get_random_user_agent() -> str:
    """获取随机User-Agent"""
    import random
    return random.choice(USER_AGENTS)


def parse_iso_date(value: Optional[str]):
    """解析 YYYY-MM-DD（兼容含时间/后缀），失败返回 None。"""
    if not value:
        return None
    try:
        return datetime.strptime(value.strip()[:10], "%Y-%m-%d").date()
    except (ValueError, TypeError):
        return None


def fetch_ssq_page(page_no: int = 1, page_size: int = 100) -> Dict:
    """
    获取双色球一页数据
    API: https://www.cwl.gov.cn/cwl_admin/front/cwlkj/search/kjxx/findDrawNotice
    """
    params = {
        "name": "ssq",
        "issueCount": "",
        "issueStart": "",
        "issueEnd": "",
        "dayStart": "",
        "dayEnd": "",
        "pageNo": str(page_no),
        "pageSize": str(page_size),
        "week": "",
        "systemType": "PC"
    }

    url = f"{SSQ_API_URL}?{urllib.parse.urlencode(params)}"
    headers = {
        "User-Agent": get_random_user_agent(),
        "Referer": "https://www.cwl.gov.cn/ygkj/wqkjgg/ssq/",
        "Accept": "application/json, text/javascript, */*; q=0.01",
    }

    req = urllib.request.Request(url, headers=headers)
    try:
        handler = urllib.request.ProxyHandler(PROXIES) if PROXIES else urllib.request.BaseHandler()
        opener = urllib.request.build_opener(handler)
        with opener.open(req, timeout=30) as response:
            data = json.loads(response.read().decode("utf-8"))
            return data
    except Exception as e:
        print(f"[ERROR] 获取双色球第{page_no}页失败: {e}", file=sys.stderr)
        return {"state": -1, "result": [], "total": 0}


def fetch_ssq_history(since_date: Optional[str] = None) -> List[Dict]:
    """
    获取双色球历史数据。
    since_date 非空时仅抓取开奖日期 >= since_date 的记录（API 返回新→旧，遇到更早的即停止翻页）。
    """
    all_draws = []
    page_no = 1
    page_size = 100
    total = None
    since = parse_iso_date(since_date)

    print("开始获取双色球历史数据..." if since is None
          else f"开始增量获取双色球数据（开奖日期 >= {since_date}）...")

    while True:
        data = fetch_ssq_page(page_no, page_size)

        if data.get("state") != 0:
            print(f"[ERROR] API返回错误: {data.get('message', '未知错误')}", file=sys.stderr)
            break

        if total is None:
            total = data.get("total", 0)
            print(f"总计 {total} 条记录，预计 {(total + page_size - 1) // page_size} 页")

        result = data.get("result", [])
        if not result:
            break

        reached_old = False
        for item in result:
            draw = parse_ssq_draw(item)
            if not draw:
                continue
            draw_date = parse_iso_date(draw.get("draw_date"))
            if since is not None and draw_date is not None and draw_date < since:
                reached_old = True
                break
            all_draws.append(draw)

        if since is None:
            print(f"  已获取 {len(all_draws)}/{total} 条记录")
        else:
            print(f"  已获取 {len(all_draws)} 条增量记录")

        if reached_old:
            print("  已到达已同步日期，停止翻页")
            break
        if since is None and len(all_draws) >= total:
            break

        page_no += 1
        time.sleep(0.5)  # 避免请求过快

    print(f"双色球数据获取完成，共 {len(all_draws)} 条记录")
    return all_draws


def parse_ssq_draw(item: Dict) -> Optional[Dict]:
    """
    解析双色球开奖记录
    """
    try:
        code = item.get("code", "")
        date_str = item.get("date", "").split("(")[0]  # 去掉星期信息

        red_str = item.get("red", "")
        blue_str = item.get("blue", "")

        red_numbers = [int(x) for x in red_str.split(",") if x.strip()]
        blue_numbers = [int(x) for x in blue_str.split(",") if x.strip()]

        sales = int(item.get("sales", 0))
        pool = int(item.get("poolmoney", 0))

        prize_grades = item.get("prizegrades", [])

        return {
            "draw_number": code,
            "draw_date": date_str,
            "red_numbers": red_numbers,
            "blue_numbers": blue_numbers,
            "sales_amount": sales,
            "pool_money": pool,
            "prize_grades": prize_grades,
            "prize_description": item.get("content", "")
        }
    except Exception as e:
        print(f"[WARN] 解析记录失败: {e}", file=sys.stderr)
        return None


def fetch_dlt_page(page_no: int = 1, page_size: int = 100) -> Dict:
    """
    获取大乐透一页数据
    API: https://webapi.sporttery.cn/gateway/lottery/getHistoryPageListV1.qry
    """
    params = {
        "gameNo": "85",  # 大乐透游戏编号
        "provinceId": "0",
        "pageSize": str(page_size),
        "isVerify": "1",
        "pageNo": str(page_no)
    }

    url = f"{DLT_API_URL}?{urllib.parse.urlencode(params)}"
    headers = {
        "User-Agent": get_random_user_agent(),
        "Referer": "https://www.lottery.gov.cn/",
        "Accept": "application/json",
    }

    req = urllib.request.Request(url, headers=headers)
    try:
        handler = urllib.request.ProxyHandler(PROXIES) if PROXIES else urllib.request.BaseHandler()
        opener = urllib.request.build_opener(handler)
        with opener.open(req, timeout=30) as response:
            data = json.loads(response.read().decode("utf-8"))
            return data
    except Exception as e:
        print(f"[ERROR] 获取大乐透第{page_no}页失败: {e}", file=sys.stderr)
        return {"value": {"list": [], "total": 0}}


def fetch_dlt_history(since_date: Optional[str] = None) -> List[Dict]:
    """
    获取大乐透历史数据。
    since_date 非空时仅抓取开奖日期 >= since_date 的记录（API 返回新→旧，遇到更早的即停止翻页）。
    """
    all_draws = []
    page_no = 1
    page_size = 100
    total = None
    since = parse_iso_date(since_date)

    print("开始获取大乐透历史数据..." if since is None
          else f"开始增量获取大乐透数据（开奖日期 >= {since_date}）...")

    while True:
        data = fetch_dlt_page(page_no, page_size)

        value = data.get("value", {})
        if total is None:
            total = value.get("total", 0)
            print(f"总计 {total} 条记录，预计 {(total + page_size - 1) // page_size} 页")

        result_list = value.get("list", [])
        if not result_list:
            break

        reached_old = False
        for item in result_list:
            draw = parse_dlt_draw(item)
            if not draw:
                continue
            draw_date = parse_iso_date(draw.get("draw_date"))
            if since is not None and draw_date is not None and draw_date < since:
                reached_old = True
                break
            all_draws.append(draw)

        if since is None:
            print(f"  已获取 {len(all_draws)}/{total} 条记录")
        else:
            print(f"  已获取 {len(all_draws)} 条增量记录")

        if reached_old:
            print("  已到达已同步日期，停止翻页")
            break
        if since is None and len(all_draws) >= total:
            break

        page_no += 1
        time.sleep(0.5)

    print(f"大乐透数据获取完成，共 {len(all_draws)} 条记录")
    return all_draws


# 大乐透奖级名 -> 数字奖级（覆盖 2026-01-31 改版前的九等奖与改版后的七等奖）
DLT_PRIZE_LEVEL_MAP = {
    "一等奖": 1, "二等奖": 2, "三等奖": 3, "四等奖": 4, "五等奖": 5,
    "六等奖": 6, "七等奖": 7, "八等奖": 8, "九等奖": 9,
}


def parse_dlt_prize_grades(prize_level_list) -> List[Dict]:
    """
    将大乐透 prizeLevelList 归一化为统一格式 [{type, typemoney}]（与双色球 prizegrades 一致）。
    - 跳过“追加”条目（奖级判定仅针对基本投注）
    - typemoney 取 stakeAmountFormat；无人中奖（-1）时记 "0"
    """
    grades = []
    if not prize_level_list:
        return grades

    for item in prize_level_list:
        name = (item.get("prizeLevel") or "").strip()
        if not name or "追加" in name:
            continue

        level = DLT_PRIZE_LEVEL_MAP.get(name)
        if level is None:
            continue

        amount = (item.get("stakeAmountFormat") or "").strip()
        try:
            amount_value = int(float(amount))
        except (TypeError, ValueError):
            amount_value = 0
        if amount_value < 0:
            amount_value = 0

        grades.append({
            "type": level,
            "typenum": item.get("stakeCount", ""),
            "typemoney": str(amount_value),
        })

    grades.sort(key=lambda g: g["type"])
    return grades


def parse_dlt_draw(item: Dict) -> Optional[Dict]:
    """
    解析大乐透开奖记录
    API返回格式: "02 17 20 29 33 08 09" (空格分隔，前5个=前区，后2个=后区)
    或带+号: "02 17 20 29 33+08 09"
    """
    try:
        draw_number = item.get("lotteryDrawNum", "")
        draw_date = item.get("lotteryDrawTime", "").split(" ")[0]

        result_str = item.get("lotteryDrawResult", "").strip()
        
        # 支持两种格式：带+号分隔 或 纯空格分隔
        if "+" in result_str:
            parts = result_str.split("+")
            front_numbers = [int(x) for x in parts[0].split() if x.strip()]
            back_numbers = [int(x) for x in parts[1].split() if x.strip()] if len(parts) > 1 else []
        else:
            # 纯空格分隔：前5个是前区，后2个是后区
            all_nums = [int(x) for x in result_str.split() if x.strip()]
            front_numbers = all_nums[:5]
            back_numbers = all_nums[5:7] if len(all_nums) >= 7 else []

        sales = int(float(item.get("lotterySaleAmount", "0")))
        pool = int(float(item.get("lotteryPoolBalanceAfterDraw", "0")))

        prize_grades = parse_dlt_prize_grades(item.get("prizeLevelList", []))

        return {
            "draw_number": draw_number,
            "draw_date": draw_date,
            "red_numbers": front_numbers,  # 前区
            "blue_numbers": back_numbers,  # 后区
            "sales_amount": sales,
            "pool_money": pool,
            "prize_grades": prize_grades,  # 归一化为 [{type, typemoney}]
            "prize_description": ""
        }
    except Exception as e:
        print(f"[WARN] 解析大乐透记录失败: {e}", file=sys.stderr)
        return None


def save_to_json(draws: List[Dict], lottery_type: str, output_dir: str = "."):
    """
    保存数据到JSON文件
    """
    os.makedirs(output_dir, exist_ok=True)
    filename = os.path.join(output_dir, f"{lottery_type}_history.json")

    with open(filename, "w", encoding="utf-8") as f:
        json.dump(draws, f, ensure_ascii=False, indent=2)

    print(f"数据已保存到: {filename}")
    return filename


def save_to_csv(draws: List[Dict], lottery_type: str, output_dir: str = "."):
    """
    保存数据到CSV文件
    """
    import csv

    os.makedirs(output_dir, exist_ok=True)
    filename = os.path.join(output_dir, f"{lottery_type}_history.csv")

    with open(filename, "w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f)
        # 写入表头
        writer.writerow([
            "期号", "开奖日期", "红球/前区", "蓝球/后区",
            "销售额", "奖池金额"
        ])

        for draw in draws:
            red_str = ",".join(str(x) for x in draw["red_numbers"])
            blue_str = ",".join(str(x) for x in draw["blue_numbers"])
            writer.writerow([
                draw["draw_number"],
                draw["draw_date"],
                red_str,
                blue_str,
                draw["sales_amount"],
                draw["pool_money"]
            ])

    print(f"数据已保存到: {filename}")
    return filename


def main():
    parser = argparse.ArgumentParser(description="彩票历史数据爬取工具")
    parser.add_argument("--type", choices=["ssq", "dlt", "all"], default="all",
                        help="彩种类型: ssq=双色球, dlt=大乐透, all=全部")
    parser.add_argument("--format", choices=["json", "csv", "both"], default="both",
                        help="输出格式")
    parser.add_argument("--output", default="./data",
                        help="输出目录")
    parser.add_argument("--since-date", default=None,
                        help="增量同步起始开奖日期（YYYY-MM-DD，含当天）；缺省=全量抓取")

    args = parser.parse_args()

    all_data = {}

    if args.type in ["ssq", "all"]:
        ssq_draws = fetch_ssq_history(args.since_date)
        all_data["ssq"] = ssq_draws

        if args.format in ["json", "both"]:
            save_to_json(ssq_draws, "ssq", args.output)
        if args.format in ["csv", "both"]:
            save_to_csv(ssq_draws, "ssq", args.output)

    if args.type in ["dlt", "all"]:
        dlt_draws = fetch_dlt_history(args.since_date)
        all_data["dlt"] = dlt_draws

        if args.format in ["json", "both"]:
            save_to_json(dlt_draws, "dlt", args.output)
        if args.format in ["csv", "both"]:
            save_to_csv(dlt_draws, "dlt", args.output)

    print("\n=== 数据获取完成 ===")
    if "ssq" in all_data:
        print(f"双色球: {len(all_data['ssq'])} 期")
    if "dlt" in all_data:
        print(f"大乐透: {len(all_data['dlt'])} 期")


if __name__ == "__main__":
    main()
