// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { type KeyboardEvent, type MouseEvent } from 'react';
import { makeStyles, mergeClasses, useArrowNavigationGroup } from '@fluentui/react-components';
import { ChevronRight16Regular } from '@fluentui/react-icons';
import { useReportContext } from '../core/ReportContext';
import { ScoreNode } from '../core/Summary';
import { useReportStyles } from '../styles/reportStyles';
import { StatusPill } from '../styles/StatusPill';

const useLocalStyles = makeStyles({
    tocRow: {
        background: 'transparent',
        transition:
            'backdrop-filter var(--duration-fast) var(--curve-easy-ease), ' +
            '-webkit-backdrop-filter var(--duration-fast) var(--curve-easy-ease)',
        ':hover': {
            WebkitBackdropFilter: 'var(--eval-nav-bd-hover)',
            backdropFilter: 'var(--eval-nav-bd-hover)',
        },
        ':focus-visible': {
            boxShadow:
                '0 0 0 2px var(--focus-stroke-inner) inset, ' +
                '0 0 0 4px var(--focus-stroke-outer) inset',
            borderRadius: 'inherit',
            outline: 'none',
        },
        '@media (forced-colors: active)': {
            ':focus-visible': {
                boxShadow: 'none',
                outline: '2px solid Highlight',
                outlineOffset: '-2px',
            },
        },
    },
    tocRowSelected: {
        WebkitBackdropFilter: 'var(--eval-nav-bd-sel)',
        backdropFilter: 'var(--eval-nav-bd-sel)',
        ':hover': {
            WebkitBackdropFilter: 'var(--eval-nav-bd-sel)',
            backdropFilter: 'var(--eval-nav-bd-sel)',
        },
    },
    caretButton: {
        appearance: 'none',
        padding: 0,
        margin: 0,
        border: 'none',
        background: 'transparent',
        cursor: 'pointer',
        color: 'inherit',
        outlineStyle: 'none',
        minWidth: '24px',
        minHeight: '24px',
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        marginTop: '-4px',
        marginBottom: '-4px',
        marginLeft: '-4px',
        marginRight: '-4px',
    },
    caretOpen: { transform: 'rotate(90deg)' },
    labelGroup: { textTransform: 'uppercase', letterSpacing: '0.5px' },
    labelStrong: { fontWeight: 'var(--font-weight-semibold)' },
    labelSelected: { color: 'var(--neutral-foreground-1)' },
    labelDefault: { color: 'var(--neutral-foreground-2)' },
    labelGroupColor: { color: 'var(--neutral-foreground-3)' },
    pillSlot: {
        flex: 'none',
        display: 'inline-flex',
        justifyContent: 'flex-end',
        minWidth: '48px',
        fontVariantNumeric: 'tabular-nums',
    },
    empty: {
        fontSize: 'var(--font-size-200)',
        color: 'var(--neutral-foreground-3)',
        padding: 'var(--spacing-xxs) var(--spacing-m-nudge)',
    },
});

type SidebarRowVM = {
    key: string;
    label: string;
    depth: number;
    hasChildren: boolean;
    expanded: boolean;
    selected: boolean;
    isTopGroup: boolean;
    passing: number;
    total: number;
    posInSet: number;
    setSize: number;
    onSelect: () => void;
    onToggle: () => void;
};

const DEPTH_PAD = [
    'var(--spacing-m-nudge)',
    'var(--spacing-xxl)',
    'var(--spacing-xxxl)',
    'calc(var(--spacing-xxxl) + var(--spacing-s))',
    'calc(var(--spacing-xxxl) + var(--spacing-l))',
] as const;
const padForDepth = (depth: number) => DEPTH_PAD[Math.min(depth, DEPTH_PAD.length - 1)];

const pillProps = (passing: number, total: number): { solid: string; textColor: string; appearance: 'ghost' | 'tint' } => {
    if (total === 0 || passing >= total) {
        return {
            appearance: 'ghost',
            solid: 'var(--neutral-solid)',
            textColor: 'var(--neutral-text)',
        };
    }

    const failed = passing / total < 0.5;
    return {
        appearance: 'tint',
        solid: failed ? 'var(--negative-solid)' : 'var(--warning-solid)',
        textColor: failed ? 'var(--negative-text)' : 'var(--caution-text)',
    };
};

type SidebarTreeProps = {
    labelledBy: string;
    expanded: ReadonlySet<string>;
    onToggle: (key: string) => void;
    onSelect?: () => void;
};

export const SidebarTree = ({ labelledBy, expanded, onToggle, onSelect }: SidebarTreeProps) => {
    const local = useLocalStyles();
    const treeNav = useArrowNavigationGroup({ axis: 'vertical' });
    const { activeNode, selectedScenarioLevel, selectScenarioLevel } = useReportContext();

    const selectScope = (nodeKey: string | undefined) => {
        const target = nodeKey ?? '';
        if (target !== (selectedScenarioLevel ?? '')) {
            selectScenarioLevel(target);
        }
        onSelect?.();
    };

    // "All scenarios" occupies position 1 of the top-level set; top-level branches follow it.
    const topBranchCount = activeNode.childNodes.filter((n) => n.hasChildNodes).length;

    const rows: SidebarRowVM[] = [];
    const walk = (nodes: ScoreNode[], depth: number, posOffset: number, setSize: number) => {
        const branches = nodes.filter((n) => n.hasChildNodes);
        const sorted = [...branches].sort((a, b) => a.name.localeCompare(b.name));
        sorted.forEach((node, i) => {
            const hasChildren = node.childNodes.some((c) => c.hasChildNodes);
            const isExpanded = expanded.has(node.nodeKey);
            rows.push({
                key: node.nodeKey,
                label: node.name,
                depth,
                hasChildren,
                expanded: isExpanded,
                selected: selectedScenarioLevel === node.nodeKey,
                isTopGroup: depth === 0 && hasChildren,
                passing: node.numPassingIterations,
                total: node.numPassingIterations + node.numFailingIterations,
                posInSet: posOffset + i + 1,
                setSize,
                onSelect: () => selectScope(node.nodeKey),
                onToggle: () => onToggle(node.nodeKey),
            });
            if (hasChildren && isExpanded) {
                const childSetSize = node.childNodes.filter((c) => c.hasChildNodes).length;
                walk(node.childNodes, depth + 1, 0, childSetSize);
            }
        });
    };
    walk(activeNode.childNodes, 0, 1, topBranchCount + 1);

    return (
        <div
            role="tree"
            aria-labelledby={labelledBy}
            {...treeNav}
        >
            <SidebarRow
                label="All scenarios"
                depth={0}
                hasChildren={false}
                expanded={false}
                selected={!selectedScenarioLevel}
                isTopGroup={false}
                posInSet={1}
                setSize={topBranchCount + 1}
                onSelect={() => selectScope(undefined)}
            />

            {rows.length === 0 ? (
                <div className={local.empty}>No scenarios</div>
            ) : (
                rows.map((row) => (
                    <SidebarRow
                        key={row.key}
                        label={row.label}
                        depth={row.depth}
                        hasChildren={row.hasChildren}
                        expanded={row.expanded}
                        selected={row.selected}
                        isTopGroup={row.isTopGroup}
                        passing={row.passing}
                        total={row.total}
                        posInSet={row.posInSet}
                        setSize={row.setSize}
                        onSelect={row.onSelect}
                        onToggle={row.onToggle}
                    />
                ))
            )}
        </div>
    );
};

const SidebarRow = ({
    label,
    depth,
    hasChildren,
    expanded,
    selected,
    isTopGroup,
    passing,
    total,
    posInSet,
    setSize,
    onSelect,
    onToggle,
}: {
    label: string;
    depth: number;
    hasChildren: boolean;
    expanded: boolean;
    selected: boolean;
    isTopGroup: boolean;
    passing?: number;
    total?: number;
    posInSet: number;
    setSize: number;
    onSelect: () => void;
    onToggle?: () => void;
}) => {
    const s = useReportStyles();
    const local = useLocalStyles();

    const onRowKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
        if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') {
            e.preventDefault();
            onSelect();
            return;
        }

        const treeItems = () => {
            const container = e.currentTarget.closest('[role="tree"]');
            return container ? Array.from(container.querySelectorAll<HTMLElement>('[role="treeitem"]')) : [];
        };

        if (e.key === 'Home' || e.key === 'End') {
            const items = treeItems();
            if (items.length > 0) {
                e.preventDefault();
                (e.key === 'Home' ? items[0] : items[items.length - 1]).focus();
            }
            return;
        }

        if (e.key === 'ArrowRight') {
            if (hasChildren && onToggle && !expanded) {
                e.preventDefault();
                onToggle();
            } else if (hasChildren && expanded) {
                const items = treeItems();
                const index = items.indexOf(e.currentTarget);
                if (index >= 0 && index + 1 < items.length) {
                    e.preventDefault();
                    items[index + 1].focus();
                }
            }
            return;
        }
        if (e.key === 'ArrowLeft') {
            if (hasChildren && onToggle && expanded) {
                e.preventDefault();
                onToggle();
            } else {
                const items = treeItems();
                const index = items.indexOf(e.currentTarget);
                const currentLevel = depth + 1;
                for (let i = index - 1; i >= 0; i--) {
                    const level = Number(items[i].getAttribute('aria-level'));
                    if (level < currentLevel) {
                        e.preventDefault();
                        items[i].focus();
                        break;
                    }
                }
            }
        }
    };

    const onCaretClick = (e: MouseEvent<HTMLButtonElement>) => {
        e.stopPropagation();
        onToggle?.();
    };

    const labelColor = selected
        ? local.labelSelected
        : isTopGroup
            ? local.labelGroupColor
            : local.labelDefault;

    return (
        <div
            role="treeitem"
            tabIndex={0}
            aria-level={depth + 1}
            aria-selected={selected}
            aria-setsize={setSize}
            aria-posinset={posInSet}
            {...(hasChildren ? { 'aria-expanded': expanded } : {})}
            title={label}
            className={mergeClasses(s.sidebarItem, local.tocRow, selected && local.tocRowSelected)}
            style={{ paddingLeft: padForDepth(depth) }}
            onClick={onSelect}
            onKeyDown={onRowKeyDown}
        >
            {hasChildren ? (
                <button
                    type="button"
                    tabIndex={-1}
                    className={mergeClasses(s.sidebarCaret, local.caretButton, expanded && local.caretOpen)}
                    onClick={onCaretClick}
                    aria-label={expanded ? `Collapse ${label}` : `Expand ${label}`}
                >
                    <ChevronRight16Regular />
                </button>
            ) : (
                <span className={s.sidebarCaret} aria-hidden="true" />
            )}
            <span
                className={mergeClasses(
                    s.sidebarItemLabel,
                    isTopGroup && local.labelGroup,
                    (selected || isTopGroup) && local.labelStrong,
                    labelColor,
                )}
            >
                {label}
            </span>
            {total !== undefined && total > 0 && (
                <span className={local.pillSlot}>
                    <StatusPill {...pillProps(passing ?? 0, total)} shape="circular">
                        {`${passing ?? 0}/${total}`}
                    </StatusPill>
                </span>
            )}
        </div>
    );
};
