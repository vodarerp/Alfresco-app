SELECT COUNT(DISTINCT n.id) AS total_folders
FROM alf_node n
JOIN alf_child_assoc ca ON n.id = ca.child_node_id
JOIN alf_node parent ON ca.parent_node_id = parent.id
WHERE n.type_qname_id = (SELECT id FROM alf_qname WHERE local_name = 'folder')
AND parent.uuid = '8ccc0f18-5445-4358-8c0f-185445235836';



WITH RECURSIVE folder_hierarchy AS (
    SELECT id, uuid, 0 AS level
    FROM alf_node
    WHERE uuid = '8ccc0f18-5445-4358-8c0f-185445235836'
    
    UNION ALL
    
    SELECT n.id, n.uuid, fh.level + 1 AS level
    FROM alf_node n
    JOIN alf_child_assoc ca ON n.id = ca.child_node_id
    JOIN folder_hierarchy fh ON ca.parent_node_id = fh.id
    WHERE n.type_qname_id = (SELECT id FROM alf_qname WHERE local_name = 'folder')
)
SELECT 
    COUNT(*) - 1 AS total_subfolders,
    MAX(level) AS max_depth
FROM folder_hierarchy;



WITH RECURSIVE folder_hierarchy AS (
    SELECT id, uuid
    FROM alf_node
    WHERE uuid = '8ccc0f18-5445-4358-8c0f-185445235836'
    
    UNION ALL
    
    SELECT n.id, n.uuid
    FROM alf_node n
    JOIN alf_child_assoc ca ON n.id = ca.child_node_id
    JOIN folder_hierarchy fh ON ca.parent_node_id = fh.id
    WHERE n.type_qname_id = (SELECT id FROM alf_qname WHERE local_name = 'folder')
)
SELECT COUNT(*) AS total_filtered_folders
FROM folder_hierarchy fh
JOIN alf_node_properties np ON fh.id = np.node_id
JOIN alf_qname q ON np.qname_id = q.id
WHERE q.local_name = 'name'
AND np.string_value LIKE '%-%';  -- npr. folderi koji sadrže crticu