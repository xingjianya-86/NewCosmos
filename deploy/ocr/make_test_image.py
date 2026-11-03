#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""合成测试图生成（不含真实 PII，仅验证管线通断与字段抽取）
用法: python make_test_image.py [id|bank]  默认两张都生成"""
import sys
from PIL import Image, ImageDraw

which = sys.argv[1] if len(sys.argv) > 1 else "all"


def make_id():
    img = Image.new("RGB", (856, 540), "#f0f0f0")
    d = ImageDraw.Draw(img)
    d.rectangle([20, 20, 836, 520], outline="#333", width=2)
    d.text((60, 120), "Name: Zhang San", fill="black")
    d.text((60, 220), "ID Card No: 11010119900307651X", fill="black")
    img.save("/tmp/test_id.png")
    print("written: /tmp/test_id.png")


def make_bank():
    img = Image.new("RGB", (856, 540), "#e8f0ff")
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([20, 20, 836, 520], radius=24, outline="#2255aa", width=2)
    d.text((60, 120), "BANK OF CHINA", fill="#2255aa")
    d.text((60, 300), "6217 0038 1001 2345 678", fill="#112244")
    d.text((60, 420), "Cardholder: Zhang San", fill="#112244")
    img.save("/tmp/test_bank.png")
    print("written: /tmp/test_bank.png")


if which in ("id", "all"):
    make_id()
if which in ("bank", "all"):
    make_bank()
