---
name: new-html-element
description: "Add a new HTML or SVG element type to the Iwesun.Runtime.Web element catalog. Use when implementing element definitions, properties, or events."
---

# Add New HTML/SVG Element

## Element Definition Rules

Every element in `src/Iwesun.Runtime.Web/Elements/` MUST:

1. Be a `sealed class` inheriting from `HtmlSvgElementBase` (or appropriate base class)
2. Implement **13 readonly semantic properties** that define the element's inherent nature:
   - `TagName`: The HTML/SVG tag name (lowercase)
   - `Category`: Element content category (Flow, Phrasing, Interactive, etc.)
   - `VisualKind`: How the element renders (Block, Inline, Table, etc.)
   - `IsVoid`: True for self-closing elements (br, img, input, etc.)
   - `ContentModel`: What content is allowed inside
   - And 8 more properties as defined in `HtmlSvgElementBase`

3. Be registered in `HtmlSvgElementCatalog`
4. Have corresponding unit tests in `tests/Iwesun.Runtime.Web.Tests/`

## Slot Property Rules

Each element has property objects with **six slots**:
1. Source initialization
2. Source link description
3. Source runtime value
4. XAML initialization
5. XAML link description
6. XAML runtime value

**Critical**: DOM absolute coordinates MUST go to source runtime slots ONLY - never write them to XAML initialization slots.

## FillAsync and ToXaml Order

Follow this exact order:
1. **Tree building first**: Only use DOM identity to build the tree structure - NO speculative styles during tree building
2. **Evidence-driven filling**: Each slot issues a strongly-typed `ElementFillRequest`
3. **Fail fast**: If required evidence returns `SourceUnsupported`, fail immediately - never fake success
4. **Recursive FillAsync**: Fill current element first, then recursively fill children in DOM order
5. **Recursive ToXaml**: Traverse the same tree, each concrete element projects all properties

## Layout Contracts

For elements with layout capabilities:
- Use strong types: `LayoutSource`, `LayoutConstraint`, `ReferenceBox`, `Axis`, `PercentageBasis`
- **NEVER** store CSS `display`, `flex-direction`, `grid-template` as raw strings
- All layout information comes from evidence filling, not speculation

## Audit Requirements

Every element must have:
- Property self-audit returning structured pass/fail/skip results
- No skipping properties or narrowing scope to fake audit success
- Statistics accumulation separated from text reporting

## Common Pitfalls to Avoid

- **Mutable singletons**: Catalog must return NEW element instances each time - never cache shared objects
- **Speculative styles during tree building**: Only use DOM identity information during tree construction
- **Slot confusion**: Writing DOM runtime coordinates to XAML initialization slots
- **String layouts**: Storing Flex/Grid layout as raw CSS strings instead of strong types
- **Audit fraud**: Skipping properties or narrowing scope to manufacture passing results
- **Dependency creep**: Introducing WinUI, DoubaoUIClone, or site-specific namespaces
