---
# 2026/11/06 12:53:00 UTC - PluginUI
uid: PluginUI.Automation
summary: *content
---
Accessibility: what screen readers (Narrator, NVDA, JAWS) see of the interface. Each element describes itself through
an <xref:PluginUI.Automation.AutomationPeer> (role, name, state) and the patterns it supports
(<xref:PluginUI.Automation.IInvokePattern>, <xref:PluginUI.Automation.IRangeValuePattern>,
<xref:PluginUI.Automation.ITogglePattern>…); PluginUI exposes the tree through Windows UI Automation. Override
<xref:PluginUI.UIElement.OnCreateAutomationPeer> to describe a custom control. Guide: docs/ACCESSIBILITY.md.
